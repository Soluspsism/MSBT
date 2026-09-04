using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace MSBT;

internal sealed partial class Renderer
{
    public void Draw()
    {
        if (plugin.Configuration.AutoSwitchPresets)
        {
            var player = Service.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.ICharacter;
            if (player != null && player.ClassJob.RowId != lastJobId)
            {
                lastJobId = player.ClassJob.RowId;
                if (plugin.Configuration.ClassPresets.TryGetValue(lastJobId, out string? profileName) && profileName != null)
                {
                    if (plugin.Configuration.SavedPresets.TryGetValue(profileName, out string? base64) && base64 != null)
                        plugin.ImportConfiguration(base64);
                }
            }
        }

        plugin.WindowSystem.Draw();

        if (plugin.IsEditMode)
            DrawEditMode();

        lock (plugin.TextNodesGate)
        {
            float realDelta = ImGui.GetIO().DeltaTime;

            passiveAuraTimer += realDelta;
            bool refreshTrackedStatuses = passiveAuraTimer >= 0.2f;
            if (refreshTrackedStatuses)
            {
                passiveAuraTimer = 0f;
                statusTargetCache.Clear();
                UpdatePassiveAuras();
                UpdateGenericTrackers();
            }

            if (plugin.CustomTexts.Count > 0)
            {
                float globalLerpFactor = 1.0f - MathF.Exp(-15.0f * realDelta);

                for (int i = plugin.CustomTexts.Count - 1; i >= 0; i--)
                {
                    var node = plugin.CustomTexts[i];
                    if (!node.IsActive) continue;

                    var ch = node.Channel;
                    if (ch == null) { plugin.ReleaseTextNodeAt(i); continue; }

                    node.Timer += realDelta;
                    bool isTrackerOrOverlay = (ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay);

                    if (isTrackerOrOverlay && node.StatusId == 0 && node.MaxDuration > 0)
                    {
                        node.RemainingTime -= realDelta;
                        if (node.RemainingTime > 0)
                        {
                            if (node.Timer > 0.35f) node.Timer = 0.35f;
                            if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.Text && ch.ShowStatusDuration)
                            {
                                int currentSecs = (int)node.RemainingTime;
                                if (currentSecs != (int)(node.RemainingTime + realDelta))
                                {
                                    string durStr = node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m {(int)(node.RemainingTime % 60)}s" : $"{node.RemainingTime:F0}s";
                                    node.Text = ch.IconOnRight ? $"({durStr}) {node.BaseText ?? ""}" : $"{node.BaseText ?? ""} ({durStr})";
                                }
                            }
                        }
                        else if (!node.IsFading) { node.IsFading = true; node.Timer = Math.Max(node.Timer, ch.Duration - 0.4f); }
                    }

                    if (isTrackerOrOverlay && node.StatusId > 0 && node.RemainingTime > 0)
                        node.RemainingTime = Math.Max(0, node.RemainingTime - realDelta);

                    if (refreshTrackedStatuses && isTrackerOrOverlay && node.StatusId > 0 && node.TargetObjectId > 0 && !node.IsFading)
                    {
                        if (!statusTargetCache.TryGetValue(node.TargetObjectId, out var obj))
                        {
                            obj = Service.ObjectTable.SearchById(node.TargetObjectId) as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
                            statusTargetCache[node.TargetObjectId] = obj;
                        }
                        bool isActive = false;
                        if (obj != null)
                        {
                            foreach (var status in obj.StatusList)
                            {
                                if (status.StatusId == node.StatusId)
                                {
                                    isActive = true;
                                    if (node.MaxDuration <= 0f || status.RemainingTime > node.RemainingTime + 0.5f) node.MaxDuration = status.RemainingTime;
                                    node.RemainingTime = status.RemainingTime;

                                    if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.Text)
                                    {
                                        if (ch.ShowStatusDuration && status.RemainingTime > 0 && status.RemainingTime < 9000f)
                                        {
                                            int currentSecs = (int)status.RemainingTime;
                                            if (currentSecs != (int)(status.RemainingTime + realDelta))
                                            {
                                                string durStr = status.RemainingTime >= 60f ? $"{(int)(status.RemainingTime / 60)}m {(int)(status.RemainingTime % 60)}s" : $"{status.RemainingTime:F0}s";
                                                node.Text = ch.IconOnRight ? $"({durStr}) {node.BaseText ?? ""}" : $"{node.BaseText ?? ""} ({durStr})";
                                            }
                                        }
                                        else node.Text = node.BaseText ?? "";
                                    }
                                    break;
                                }
                            }
                        }

                        if (isActive) { if (node.Timer > 0.35f) node.Timer = 0.35f; node.RequiresDurationCheck = false; node.IsFading = false; }
                        else if (node.Timer < 1.0f && node.MaxDuration <= 0f) { }
                        else if (!node.IsFading) { node.IsFading = true; node.Timer = Math.Max(node.Timer, ch.Duration - 0.4f); }
                    }

                    float maxLife = ch.Duration;
                    bool treatAsCrit1 = node.IsCrit || (node.IsBigHit && ch.BigHitActsAsCrit);
                    if (treatAsCrit1 && ch.CritBehavior != 0 && !node.IsAlert && !node.IsTextOnly) maxLife = ch.CritDuration;

                    if (ch.Mode == ChannelMode.Scrolling && ch.Speed > 0 && node.TargetXOffset < 0)
                    {
                        maxLife += Math.Abs(node.TargetXOffset) / ch.Speed;
                    }

                    if (node.Timer > maxLife) { plugin.ReleaseTextNodeAt(i); continue; }

                    if (isTrackerOrOverlay) { }
                    else if (ch.Mode == ChannelMode.Scrolling)
                    {
                        int lane = GetNodeLane(node, ch);
                        ScrollDirection dir = ch.Direction;
                        bool noFlight = dir == ScrollDirection.Static || dir == ScrollDirection.Pop || dir == ScrollDirection.Fade;
                        ScrollDirection flowDir = noFlight ? ScrollDirection.Up : dir;

                        float currentBump = 0f;
                        if (lane != 0)
                        {
                            float queueTravel = Math.Max(0f, -node.TargetXOffset);
                            node.DistanceTraveled += (queueTravel - node.DistanceTraveled) * globalLerpFactor;
                            currentBump = node.TargetXOffset + node.DistanceTraveled;
                        }

                        float currentX = ch.X;
                        float currentY = ch.Y;

                        if (lane == 0)
                        {
                            float flightDist = node.Timer * ch.Speed;
                            float progress = node.TargetXOffset + flightDist;

                            if (flowDir == ScrollDirection.Up) currentY -= progress;
                            else if (flowDir == ScrollDirection.Down) currentY += progress;
                            else if (flowDir == ScrollDirection.Left) currentX -= progress;
                            else if (flowDir == ScrollDirection.Right) currentX += progress;

                            if (ch.Curve != 0 && ch.Duration > 0 && !noFlight)
                            {
                                float tArc = Math.Clamp(node.Timer / ch.Duration, 0f, 1f);
                                float curveOffset = MathF.Sin(tArc * MathF.PI) * ch.Curve;
                                if (flowDir == ScrollDirection.Up || flowDir == ScrollDirection.Down) currentX += curveOffset;
                                else currentY += curveOffset;
                            }
                        }
                        else if (lane == 1)
                        {
                            if (flowDir == ScrollDirection.Up) currentY -= currentBump;
                            else if (flowDir == ScrollDirection.Down) currentY += currentBump;
                            else if (flowDir == ScrollDirection.Left) currentX -= currentBump;
                            else if (flowDir == ScrollDirection.Right) currentX += currentBump;
                        }
                        else if (lane == 2)
                        {
                            currentX += ch.CritOffsetX;
                            currentY += ch.CritOffsetY;

                            if (flowDir == ScrollDirection.Up) currentY -= currentBump;
                            else if (flowDir == ScrollDirection.Down) currentY += currentBump;
                            else if (flowDir == ScrollDirection.Left) currentX -= currentBump;
                            else if (flowDir == ScrollDirection.Right) currentX += currentBump;

                            if (ch.CritCurve != 0 && ch.Duration > 0 && ch.Speed > 0 && !noFlight)
                            {
                                float maxDist = ch.Duration * ch.Speed;
                                float baseArcProgress = Math.Clamp(currentBump / maxDist, 0f, 1f);
                                float mappedArc = ch.CritCurveStart + baseArcProgress * (ch.CritCurveEnd - ch.CritCurveStart);
                                float curveOffset = MathF.Sin(mappedArc * MathF.PI) * ch.CritCurve;

                                if (flowDir == ScrollDirection.Up || flowDir == ScrollDirection.Down) currentX += curveOffset;
                                else currentY += curveOffset;
                            }
                        }
                        else if (lane == 3)
                        {
                            float freezeTime = ch.CritLinger;
                            float flyTimeTotal = ch.CritDuration - freezeTime;
                            if (flyTimeTotal <= 0) flyTimeTotal = 0.001f;

                            float frozenX = ch.X + ch.CritOffsetX;
                            float frozenY = ch.Y + ch.CritOffsetY;

                            if (flowDir == ScrollDirection.Up) frozenY -= currentBump;
                            else if (flowDir == ScrollDirection.Down) frozenY += currentBump;
                            else if (flowDir == ScrollDirection.Left) frozenX -= currentBump;
                            else if (flowDir == ScrollDirection.Right) frozenX += currentBump;

                            if (ch.CritCurve != 0 && ch.Duration > 0 && ch.Speed > 0 && !noFlight)
                            {
                                float maxDist = ch.Duration * ch.Speed;
                                float baseArcProgress = Math.Clamp(currentBump / maxDist, 0f, 1f);
                                float mappedArc = ch.CritCurveStart + baseArcProgress * (ch.CritCurveEnd - ch.CritCurveStart);
                                float curveOffset = MathF.Sin(mappedArc * MathF.PI) * ch.CritCurve;

                                if (flowDir == ScrollDirection.Up || flowDir == ScrollDirection.Down) frozenX += curveOffset;
                                else frozenY += curveOffset;
                            }

                            if (node.Timer <= freezeTime)
                            {
                                currentX = frozenX;
                                currentY = frozenY;
                            }
                            else
                            {
                                float activeFlyTime = node.Timer - freezeTime;

                                float parallelDist = noFlight ? 0f : (activeFlyTime * ch.Speed);
                                float flyingFrozenX = frozenX;
                                float flyingFrozenY = frozenY;

                                if (flowDir == ScrollDirection.Up) flyingFrozenY -= parallelDist;
                                else if (flowDir == ScrollDirection.Down) flyingFrozenY += parallelDist;
                                else if (flowDir == ScrollDirection.Left) flyingFrozenX -= parallelDist;
                                else if (flowDir == ScrollDirection.Right) flyingFrozenX += parallelDist;

                                float streamDist = noFlight ? 0f : (node.Timer * ch.Speed);
                                float targetX = ch.X;
                                float targetY = ch.Y;

                                if (flowDir == ScrollDirection.Up) targetY -= streamDist;
                                else if (flowDir == ScrollDirection.Down) targetY += streamDist;
                                else if (flowDir == ScrollDirection.Left) targetX -= streamDist;
                                else if (flowDir == ScrollDirection.Right) targetX += streamDist;

                                if (ch.Curve != 0 && ch.Duration > 0 && !noFlight)
                                {
                                    float tArc = Math.Clamp(node.Timer / ch.Duration, 0f, 1f);
                                    float curveOffset = MathF.Sin(tArc * MathF.PI) * ch.Curve;
                                    if (flowDir == ScrollDirection.Up || flowDir == ScrollDirection.Down) targetX += curveOffset;
                                    else targetY += curveOffset;
                                }

                                float tMerge = Math.Clamp(activeFlyTime / flyTimeTotal, 0f, 1f);
                                float easeFly = tMerge * tMerge * (3f - 2f * tMerge);
                                float mergeEase = easeFly * ch.CritCurvePhase;

                                currentX = flyingFrozenX + (targetX - flyingFrozenX) * mergeEase;
                                currentY = flyingFrozenY + (targetY - flyingFrozenY) * mergeEase;
                            }
                        }

                        node.CurrentX = currentX;
                        node.CurrentY = currentY;
                    }
                }

                BuildChannelNodeBuckets();
                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
                ImGui.SetNextWindowBgAlpha(0f);

                bool overlayVisible = ImGui.Begin("MSBT_FullOverlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
                try
                {
                    if (overlayVisible)
                    {
                        try
                        {
                            var drawList = ImGui.GetWindowDrawList();
                            Vector2 viewportSize = ImGui.GetIO().DisplaySize;

                            foreach (var ch in plugin.Configuration.Channels)
                            {
                                if (!ch.Enabled || (ch.Mode != ChannelMode.Tracker && ch.Mode != ChannelMode.Overlay))
                                    continue;

                                if (!channelNodes.TryGetValue(ch, out List<CustomSCTNode>? nodes))
                                    continue;
                                float currentTotalOffsetX = 0f; float currentTotalOffsetY = 0f;

                                IDisposable? fontPusher = null;
                                float fontScaleCorrection = 1f;
                                try
                                {
                                    FontSelection font = plugin.FontManager.GetChannelFont(ch.FontKey, ch.FontSize);
                                    fontScaleCorrection = font.ScaleCorrection;
                                    if (font.Handle.Available) fontPusher = font.Handle.Push();
                                    ImGui.SetWindowFontScale(ch.NormalScale * fontScaleCorrection);

                                    for (int j = 0; j < nodes.Count; j++)
                                    {
                                        var n = nodes[j];
                                        if (!IsNodeVisible(n, ch))
                                            continue;
                                        float targetX = 0f; float targetY = 0f;

                                        if (ch.Mode == ChannelMode.Tracker)
                                        {
                                            if (ch.Direction == ScrollDirection.Left) targetX = -currentTotalOffsetX;
                                            else if (ch.Direction == ScrollDirection.Right) targetX = currentTotalOffsetX;
                                            if (ch.Direction == ScrollDirection.Up) targetY = -currentTotalOffsetY;
                                            else if (ch.Direction == ScrollDirection.Down) targetY = currentTotalOffsetY;
                                        }

                                        float absTargetX = ch.X + targetX; float absTargetY = ch.Y + targetY;

                                        if (n.IsFirstFrameTracker) { n.CurrentX = absTargetX; n.CurrentY = absTargetY; n.IsFirstFrameTracker = false; }
                                        else
                                        {
                                            n.CurrentX += (absTargetX - n.CurrentX) * globalLerpFactor;
                                            n.CurrentY += (absTargetY - n.CurrentY) * globalLerpFactor;
                                        }

                                        var iconWrap = GetIconWrap(n.IconId);
                                        bool hasIconHandle = iconWrap != null;

                                        float aspect = (hasIconHandle && iconWrap!.Height > 0) ? ((float)iconWrap.Width / iconWrap.Height) : 1.0f;

                                        if (ch.TrackerStyle == TrackerStyle.Text)
                                        {
                                            string safeTrackerText = n.Text ?? "";
                                            float iconH = (hasIconHandle && !ch.HideIcons) ? (28.0f * ch.NormalScale * ch.IconScale) : 0f;
                                            float iconW = iconH * aspect;
                                            float pad = (hasIconHandle && !string.IsNullOrEmpty(safeTrackerText)) ? 6.0f : 0f;
                                            float txtW = ImGui.CalcTextSize(safeTrackerText).X;
                                            currentTotalOffsetX += (txtW + iconW + pad + (25f * ch.NormalScale)); currentTotalOffsetY += (Math.Max(45f * ch.NormalScale, iconH) + 5f);
                                        }
                                        else if (ch.TrackerStyle == TrackerStyle.ProgressBar)
                                        {
                                            string safeSkillNameTracker = string.IsNullOrEmpty(n.SkillName) ? (n.BaseText ?? "") : n.SkillName;
                                            safeSkillNameTracker ??= "";

                                            ImGui.SetWindowFontScale(ch.NormalScale * 0.8f * fontScaleCorrection);
                                            float nameW = ImGui.CalcTextSize(safeSkillNameTracker).X;
                                            float timerW = n.MaxDuration > 0.01f ? ImGui.CalcTextSize("00.0s").X : 0f;
                                            ImGui.SetWindowFontScale(ch.NormalScale * fontScaleCorrection);

                                            float barH = 26.0f * ch.NormalScale;
                                            float iconH = (hasIconHandle && !ch.HideIcons) ? barH : 0f;
                                            float iconW = iconH * aspect;

                                            float barW = Math.Max(150.0f * ch.NormalScale, iconW + nameW + timerW + (20f * ch.NormalScale));

                                            float maxH = Math.Max(barH, iconH);
                                            currentTotalOffsetX += barW + (10f * ch.NormalScale);
                                            currentTotalOffsetY += maxH + (10f * ch.NormalScale);
                                        }
                                        else
                                        {
                                            float iconH = 40.0f * ch.NormalScale * ch.IconScale;
                                            float iconW = iconH * aspect;
                                            currentTotalOffsetX += iconW + (10f * ch.NormalScale); currentTotalOffsetY += iconH + (15f * ch.NormalScale);
                                        }
                                    }
                                }
                                finally { ImGui.SetWindowFontScale(1.0f); fontPusher?.Dispose(); }
                            }

                            void DrawNodeLayer(bool critLayer)
                            {
                                foreach (DisplayChannel channel in plugin.Configuration.Channels)
                                {
                                    if (!channelNodes.TryGetValue(channel, out List<CustomSCTNode>? nodes) || nodes.Count == 0)
                                        continue;

                                    FontSelection font = plugin.FontManager.GetChannelFont(channel.FontKey, channel.FontSize);
                                    IDisposable? fontPusher = font.Handle.Available ? font.Handle.Push() : null;
                                    try
                                    {
                                        foreach (CustomSCTNode node in nodes)
                                        {
                                            if (node.IsActive && node.IsCrit == critLayer)
                                                DrawNodeItem(node, font.ScaleCorrection, drawList, viewportSize);
                                        }
                                    }
                                    finally
                                    {
                                        ImGui.SetWindowFontScale(1f);
                                        fontPusher?.Dispose();
                                    }
                                }
                            }

                            DrawNodeLayer(false);
                            DrawNodeLayer(true);
                        }
                        catch (Exception ex)
                        {
                            Service.Log.Error(ex, "Crash prevented in MSBT Tracker Rendering!");
                        }
                    }
                }
                finally
                {
                    ImGui.End();
                }
            }
        }
    }
}
