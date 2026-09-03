using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace MSBT;

public class Renderer
{
    private readonly Plugin plugin;
    private uint lastJobId = uint.MaxValue;
    private float passiveAuraTimer = 0f;

    private readonly HashSet<uint> failedIcons = new();

    public Renderer(Plugin plugin) { this.plugin = plugin; }

    public void PlayInGameSound(int soundId)
    {
        if (soundId < 1 || soundId > 16) return;
        Plugin.Framework.RunOnFrameworkThread(() => {
            try { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect((uint)soundId); } catch { }
        });
    }

    public string FormatNumber(int value, NumberFormatType formatType)
    {
        if (formatType == NumberFormatType.Space) return value.ToString("#,0").Replace(',', ' ');
        if (formatType == NumberFormatType.Comma) return value.ToString("#,0");
        if (formatType == NumberFormatType.Smart)
        {
            if (value >= 1000000) return (value / 1000000.0).ToString("0.##") + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#") + "k";
        }
        return value.ToString();
    }

    private void DrawRadialCooldown(ImDrawListPtr drawList, Vector2 center, float radius, float radialProgress, uint color)
    {
        if (radialProgress <= 0.01f || float.IsNaN(radialProgress) || float.IsInfinity(radialProgress)) return;
        drawList.PathClear();
        drawList.PathLineTo(center);
        drawList.PathArcTo(center, radius, -1.570796f, -1.570796f + (radialProgress * 6.283185f), 32);
        drawList.PathFillConvex(color);
    }

    private int GetNodeLane(CustomSCTNode n, DisplayChannel ch)
    {
        bool treatAsCrit = n.IsCrit || (n.IsBigHit && ch.BigHitActsAsCrit);
        bool isCritStream = treatAsCrit && ch.CritBehavior != 0 && !n.IsAlert && !n.IsTextOnly;
        bool isStaticMode = ch.Direction == ScrollDirection.Static || ch.Direction == ScrollDirection.Pop || ch.Direction == ScrollDirection.Fade;

        if (isCritStream && ch.CritBehavior == 1) return 2;
        if (isCritStream && ch.CritBehavior == 2) return 3;

        return isStaticMode ? 1 : 0;
    }

    private int GetLaneFromParams(DisplayChannel ch, bool isCritStream)
    {
        bool isStaticMode = ch.Direction == ScrollDirection.Static || ch.Direction == ScrollDirection.Pop || ch.Direction == ScrollDirection.Fade;

        if (isCritStream && ch.CritBehavior == 1) return 2;
        if (isCritStream && ch.CritBehavior == 2) return 3;

        return isStaticMode ? 1 : 0;
    }

    public float GetSpawnOffsetAndBump(DisplayChannel ch, float scale, ScrollDirection dir, bool isCritStream)
    {
        if (ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay) return 0f;

        float spacing = isCritStream ? (75f * scale) : (45f * scale);
        int myLane = GetLaneFromParams(ch, isCritStream);

        if (myLane == 0)
        {
            var lastNode = plugin.CustomTexts
                .Where(x => x.IsActive && x.Channel == ch && GetNodeLane(x, ch) == 0)
                .OrderByDescending(x => x.SpawnId)
                .FirstOrDefault();

            if (lastNode != null)
            {
                float lastTravel = (lastNode.Timer * ch.Speed) + lastNode.TargetXOffset;
                if (lastTravel < spacing) return lastTravel - spacing;
            }
            return 0f;
        }
        else
        {
            var activeNodes = plugin.CustomTexts.Where(x => x.IsActive && x.Channel == ch && GetNodeLane(x, ch) == myLane).ToList();
            foreach (var n in activeNodes) n.TargetYOffset += spacing;
            return 0f;
        }
    }

    private void DrawTextWithOutline(ImDrawListPtr drawList, Vector2 pos, string text, uint color, uint outlineColor, float scale)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (plugin.Configuration.EnableOutline)
        {
            float thick = 2.0f;
            drawList.AddText(new Vector2(pos.X - thick, pos.Y - thick), outlineColor, text);
            drawList.AddText(new Vector2(pos.X + thick, pos.Y - thick), outlineColor, text);
            drawList.AddText(new Vector2(pos.X - thick, pos.Y + thick), outlineColor, text);
            drawList.AddText(new Vector2(pos.X + thick, pos.Y + thick), outlineColor, text);
            drawList.AddText(new Vector2(pos.X - thick, pos.Y), outlineColor, text);
            drawList.AddText(new Vector2(pos.X + thick, pos.Y), outlineColor, text);
            drawList.AddText(new Vector2(pos.X, pos.Y - thick), outlineColor, text);
            drawList.AddText(new Vector2(pos.X, pos.Y + thick), outlineColor, text);
        }
        else { drawList.AddText(new Vector2(pos.X + 2, pos.Y + 2), outlineColor, text); }
        drawList.AddText(pos, color, text);
    }

    private void DrawAnchorCrosshair(Vector2 pos, uint color, TextAlignment align, bool isCrit = false)
    {
        var fgDrawList = ImGui.GetForegroundDrawList();
        fgDrawList.AddCircleFilled(pos, 4f, color);
        fgDrawList.AddCircle(pos, 5f, 0xFF000000, 12, 1.5f);
        uint alphaColor = (color & 0x00FFFFFF) | 0x88000000;
        fgDrawList.AddLine(new Vector2(pos.X - 35, pos.Y), new Vector2(pos.X + 35, pos.Y), alphaColor, 1f);
        fgDrawList.AddLine(new Vector2(pos.X, pos.Y - 35), new Vector2(pos.X, pos.Y + 35), alphaColor, 1f);
        if (!isCrit)
        {
            float w = 60f; Vector2 p1 = new Vector2(pos.X, pos.Y + 10); Vector2 p2 = new Vector2(pos.X, pos.Y + 10);
            if (align == TextAlignment.Center) { p1.X -= w / 2; p2.X += w / 2; }
            else if (align == TextAlignment.Right) { p1.X -= w; } else { p2.X += w; }
            fgDrawList.AddLine(p1, p2, color, 3f);
            fgDrawList.AddLine(new Vector2(p1.X, p1.Y - 4), new Vector2(p1.X, p1.Y + 4), color, 2f);
            fgDrawList.AddLine(new Vector2(p2.X, p2.Y - 4), new Vector2(p2.X, p2.Y + 4), color, 2f);
        }
    }

    private bool IsNodeVisible(CustomSCTNode node, DisplayChannel ch)
    {
        if (node.TargetObjectId == uint.MaxValue) return true;

        if (ch.CurrentTargetOnly)
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            var currentTarget = Plugin.TargetManager.Target;

            bool isPlayer = localPlayer != null && node.TargetObjectId == localPlayer.EntityId;
            bool isCurrentTarget = currentTarget != null && node.TargetObjectId == currentTarget.EntityId;

            if (!isPlayer && !isCurrentTarget) return false;
        }
        return true;
    }

    private void SpawnPassiveNode(AuraTrigger trg, DisplayChannel ch, uint targetId, float remTime, float maxDur)
    {
        lock (plugin.CustomTexts)
        {
            var existingNode = plugin.CustomTexts.FirstOrDefault(n => n.IsActive && n.Channel == ch && n.StatusId == trg.StatusId && n.TargetObjectId == targetId);
            if (existingNode == null)
            {
                var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
                if (node == null) { node = new CustomSCTNode(); plugin.CustomTexts.Add(node); }
                string name = string.IsNullOrEmpty(trg.CustomText) ? plugin.Parser.GetSkillName(trg.StatusId) : trg.CustomText;
                uint icon = plugin.Parser.GetIconId(trg.StatusId);
                node.Init(name ?? "", name ?? "", 0, 0, false, false, false, true, false, true, ch, icon, 0, trg.StatusId, name ?? "", false, trg.StatusId, targetId, maxDur, remTime, 0);
            }
        }
    }

    private void SpawnStatusTrackerNode(DisplayChannel ch, uint statusId, uint targetId, float remTime, float maxDur)
    {
        lock (plugin.CustomTexts)
        {
            var existingNode = plugin.CustomTexts.FirstOrDefault(n => n.IsActive && n.Channel == ch && n.StatusId == statusId && n.TargetObjectId == targetId);
            if (existingNode == null)
            {
                var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
                if (node == null) { node = new CustomSCTNode(); plugin.CustomTexts.Add(node); }
                string name = plugin.Parser.GetSkillName(statusId);
                uint icon = plugin.Parser.GetIconId(statusId);
                node.Init(name ?? "", name ?? "", 0, 0, false, false, false, true, false, false, ch, icon, 0, statusId, name ?? "", false, statusId, targetId, maxDur, remTime, 0);
            }
        }
    }

    private void UpdatePassiveAuras()
    {
        var player = Plugin.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        var target = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        if (player == null) return;
        var passiveTriggers = plugin.Configuration.AuraTriggers.Where(t => t.Enabled && t.StatusId > 0).ToList();

        foreach (var trg in passiveTriggers)
        {
            var targetChannels = plugin.Configuration.Channels.Where(c => c.Enabled && (c.Mode == ChannelMode.Tracker || c.Mode == ChannelMode.Overlay) && (trg.TargetChannels.Contains(c.Name) || trg.TargetChannelName == c.Name)).ToList();
            if (targetChannels.Count == 0) continue;

            bool foundOnPlayer = false; float pRemTime = 0f; float pMaxDur = 0f;
            foreach (var status in player.StatusList)
            {
                if (status.StatusId == trg.StatusId)
                {
                    if (trg.OnlyCastByMe && status.SourceId != player.EntityId) continue;
                    foundOnPlayer = true; pRemTime = status.RemainingTime; pMaxDur = status.RemainingTime;
                    break;
                }
            }
            if (foundOnPlayer && plugin.Parser.CheckConditions(trg.Conditions, player, target))
                foreach (var ch in targetChannels) SpawnPassiveNode(trg, ch, player.EntityId, pRemTime, pMaxDur);

            if (target != null)
            {
                bool foundOnTarget = false; float tRemTime = 0f; float tMaxDur = 0f;
                foreach (var status in target.StatusList)
                {
                    if (status.StatusId == trg.StatusId)
                    {
                        if (trg.OnlyCastByMe && status.SourceId != player.EntityId) continue;
                        foundOnTarget = true; tRemTime = status.RemainingTime; tMaxDur = status.RemainingTime;
                        break;
                    }
                }
                if (foundOnTarget && plugin.Parser.CheckConditions(trg.Conditions, player, target))
                    foreach (var ch in targetChannels) SpawnPassiveNode(trg, ch, target.EntityId, tRemTime, tMaxDur);
            }
        }
    }

    private void UpdateGenericTrackers()
    {
        var player = Plugin.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        var target = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

        if (player == null) return;

        var trackerChannels = plugin.Configuration.Channels.Where(c => c.Enabled && c.Mode == ChannelMode.Tracker).ToList();
        if (trackerChannels.Count == 0) return;

        var passiveTriggers = plugin.Configuration.AuraTriggers.Where(t => t.Enabled && t.StatusId > 0).Select(t => t.StatusId).ToHashSet();

        if (target != null && target.EntityId != player.EntityId)
        {
            foreach (var status in target.StatusList)
            {
                if (passiveTriggers.Contains(status.StatusId)) continue;

                bool isFromMe = status.SourceId == player.EntityId;
                if (isFromMe)
                {
                    foreach (var ch in trackerChannels)
                    {
                        if (ch.AcceptsOutgoingStatuses)
                        {
                            SpawnStatusTrackerNode(ch, status.StatusId, target.EntityId, status.RemainingTime, status.RemainingTime);
                        }
                    }
                }
            }
        }
    }

    private IDalamudTextureWrap? GetIconWrap(uint iconId)
    {
        if (iconId == 0 || iconId == 405 || failedIcons.Contains(iconId)) return null;
        try
        {
            return Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup { IconId = iconId }).GetWrapOrDefault();
        }
        catch
        {
            failedIcons.Add(iconId);
            return null;
        }
    }

    public void Draw()
    {
        if (plugin.Configuration.AutoSwitchPresets)
        {
            var player = Plugin.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.ICharacter;
            if (player != null && player.ClassJob.RowId != lastJobId)
            {
                lastJobId = player.ClassJob.RowId;
                if (plugin.Configuration.ClassPresets.TryGetValue(lastJobId, out string? profileName) && profileName != null)
                {
                    if (plugin.Configuration.SavedPresets.TryGetValue(profileName, out string? base64) && base64 != null)
                        plugin.Configuration.ImportFromBase64(base64);
                }
            }
        }

        plugin.WindowSystem.Draw();

        if (plugin.IsEditMode)
        {
            var bgDrawList = ImGui.GetBackgroundDrawList(); var screenSize = ImGui.GetIO().DisplaySize;
            float centerX = screenSize.X / 2f; float centerY = screenSize.Y / 2f;

            uint minorGridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.04f));
            uint majorGridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.12f));
            uint centerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 0f, 0.4f));
            uint critLinkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.5f));
            uint critAnchorColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.9f));
            uint critTextColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 1f));

            int numCols = (int)(centerX / 25f) + 1;
            for (int i = 0; i <= numCols; i++) { float offset = i * 25f; uint color = (i % 4 == 0) ? majorGridColor : minorGridColor; bgDrawList.AddLine(new Vector2(centerX + offset, 0), new Vector2(centerX + offset, screenSize.Y), color, 1f); if (i > 0) bgDrawList.AddLine(new Vector2(centerX - offset, 0), new Vector2(centerX - offset, screenSize.Y), color, 1f); }
            int numRows = (int)(centerY / 25f) + 1;
            for (int i = 0; i <= numRows; i++) { float offset = i * 25f; uint color = (i % 4 == 0) ? majorGridColor : minorGridColor; bgDrawList.AddLine(new Vector2(0, centerY + offset), new Vector2(screenSize.X, centerY + offset), color, 1f); if (i > 0) bgDrawList.AddLine(new Vector2(0, centerY - offset), new Vector2(screenSize.X, centerY - offset), color, 1f); }

            bgDrawList.AddLine(new Vector2(centerX, 0), new Vector2(centerX, screenSize.Y), centerColor, 2f);
            bgDrawList.AddLine(new Vector2(0, centerY), new Vector2(screenSize.X, centerY), centerColor, 2f);

            foreach (var ch in plugin.Configuration.Channels)
            {
                if (!ch.Enabled) continue;
                ImGui.SetNextWindowPos(new Vector2(ch.X, ch.Y), ImGuiCond.Appearing); ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.05f, 0.05f, 0.05f, 0.8f));

                try
                {
                    if (ImGui.Begin($"MSBT_Anchor_{ch.Name}", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
                    {
                        string icon = ch.Mode == ChannelMode.Overlay ? "[Ovl]" : (ch.Mode == ChannelMode.Tracker ? "[Trk]" : "✥");
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), icon); ImGui.SameLine(); ImGui.Text(ch.Name);
                        if (ImGui.IsWindowFocused()) { var pos = ImGui.GetWindowPos(); ch.X = pos.X; ch.Y = pos.Y; } else { ImGui.SetWindowPos(new Vector2(ch.X, ch.Y)); }
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) plugin.Configuration.Save();
                    }
                }
                finally
                {
                    ImGui.End();
                    ImGui.PopStyleColor();
                }

                DrawAnchorCrosshair(new Vector2(ch.X, ch.Y), ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 1f, 0.4f, 1f)), ch.Alignment);

                if (ch.CritBehavior != 0 && ch.Mode == ChannelMode.Scrolling)
                {
                    bgDrawList.AddLine(new Vector2(ch.X, ch.Y), new Vector2(ch.X + ch.CritOffsetX, ch.Y + ch.CritOffsetY), critLinkColor, 2f);
                    DrawAnchorCrosshair(new Vector2(ch.X + ch.CritOffsetX, ch.Y + ch.CritOffsetY), critAnchorColor, TextAlignment.Center, true);
                    ImGui.GetForegroundDrawList().AddText(new Vector2(ch.X + ch.CritOffsetX + 10, ch.Y + ch.CritOffsetY - 15), critTextColor, "Crits");
                }
            }
        }

        lock (plugin.CustomTexts)
        {
            if (plugin.CustomTexts.Count > 0)
            {
                float realDelta = ImGui.GetIO().DeltaTime;

                // ОПТИМИЗАЦИЯ: Считаем тяжелую математику интерполяции один раз за кадр
                float globalLerpFactor = 1.0f - (float)Math.Exp(-15.0f * realDelta);

                passiveAuraTimer += realDelta;
                if (passiveAuraTimer >= 0.2f)
                {
                    passiveAuraTimer = 0f;
                    UpdatePassiveAuras();
                    UpdateGenericTrackers();
                }

                for (int i = plugin.CustomTexts.Count - 1; i >= 0; i--)
                {
                    var node = plugin.CustomTexts[i];
                    if (!node.IsActive) continue;

                    var ch = node.Channel;
                    if (ch == null) { node.IsActive = false; continue; }

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

                    if (isTrackerOrOverlay && node.StatusId > 0 && node.TargetObjectId > 0 && !node.IsFading)
                    {
                        var obj = Plugin.ObjectTable.FirstOrDefault(x => x.EntityId == node.TargetObjectId) as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
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

                    if (node.Timer > maxLife) { node.IsActive = false; continue; }

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
                            node.DistanceTraveled += (node.TargetYOffset - node.DistanceTraveled) * globalLerpFactor;
                            currentBump = node.DistanceTraveled;
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
                                float curveOffset = (float)Math.Sin(tArc * Math.PI) * ch.Curve;
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
                                float curveOffset = (float)Math.Sin(mappedArc * Math.PI) * ch.CritCurve;

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
                                float curveOffset = (float)Math.Sin(mappedArc * Math.PI) * ch.CritCurve;

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
                                    float curveOffset = (float)Math.Sin(tArc * Math.PI) * ch.Curve;
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

                ImGui.SetNextWindowPos(Vector2.Zero);
                ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
                ImGui.SetNextWindowBgAlpha(0f);

                if (ImGui.Begin("MSBT_FullOverlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
                {
                    try
                    {
                        var drawList = ImGui.GetWindowDrawList();

                        foreach (var ch in plugin.Configuration.Channels.Where(c => c.Enabled && (c.Mode == ChannelMode.Tracker || c.Mode == ChannelMode.Overlay)))
                        {
                            var activeTrackers = plugin.CustomTexts.Where(x => x.IsActive && x.Channel == ch && IsNodeVisible(x, ch)).OrderBy(x => x.SpawnId).ToList();
                            float currentTotalOffsetX = 0f; float currentTotalOffsetY = 0f;

                            IDisposable? fontPusher = null;
                            try
                            {
                                var font = plugin.FontManager.GetChannelFont(ch.FontFileName);
                                if (font != null && font.Available) fontPusher = font.Push();
                                ImGui.SetWindowFontScale(ch.NormalScale);

                                for (int j = 0; j < activeTrackers.Count; j++)
                                {
                                    var n = activeTrackers[j];
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

                                        string timerTextTracker = n.MaxDuration > 0.01f ? (n.RemainingTime >= 60f ? $"{(int)(n.RemainingTime / 60)}m {(int)(n.RemainingTime % 60)}s" : $"{n.RemainingTime:F1}s") : "";

                                        ImGui.SetWindowFontScale(ch.NormalScale * 0.8f);
                                        float nameW = ImGui.CalcTextSize(safeSkillNameTracker).X;
                                        float timerW = timerTextTracker.Length > 0 ? ImGui.CalcTextSize("00.0s").X : 0f;
                                        ImGui.SetWindowFontScale(ch.NormalScale);

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

                        void DrawNodeItem(CustomSCTNode node)
                        {
                            var ch = node.Channel;
                            if (!IsNodeVisible(node, ch)) return;

                            float maxLife = ch.Duration;
                            bool treatAsCrit2 = node.IsCrit || (node.IsBigHit && ch.BigHitActsAsCrit);
                            if (treatAsCrit2 && ch.CritBehavior != 0 && !node.IsAlert && !node.IsTextOnly) maxLife = ch.CritDuration;

                            float timeRemaining = maxLife - node.Timer;
                            float alpha = 1.0f;
                            float fadeOutTime = ch.FadeDuration;

                            if (ch.Mode == ChannelMode.Overlay && node.Timer < 0.3f) alpha = node.Timer / 0.3f;
                            else if (timeRemaining < fadeOutTime && fadeOutTime > 0)
                                alpha = Math.Clamp(timeRemaining / fadeOutTime, 0f, 1f);

                            if (ch.Mode == ChannelMode.Scrolling && GetNodeLane(node, ch) == 0)
                            {
                                float progress = node.TargetXOffset + (node.Timer * ch.Speed);
                                if (progress < 0) alpha = 0f;
                                else if (progress < 15f && ch.Speed > 0) alpha *= (progress / 15f);
                            }
                            else if (ch.Mode == ChannelMode.Scrolling && (ch.Direction == ScrollDirection.Fade || ch.Direction == ScrollDirection.Pop) && node.Timer < 0.2f)
                            {
                                alpha *= (node.Timer / 0.2f);
                            }

                            Vector4 baseColor = plugin.Configuration.ColorZone1;
                            if (node.IsAlert) baseColor = node.IsFading ? plugin.Configuration.ColorStatusFading : plugin.Configuration.ColorZone4;
                            else if (node.IsTextOnly) baseColor = node.IsFading ? plugin.Configuration.ColorStatusFading : plugin.Configuration.ColorStatus;
                            else if (node.IsMp) baseColor = plugin.Configuration.ColorMp;
                            else if (node.IsHeal) baseColor = plugin.Configuration.ColorHeal;
                            else
                            {
                                bool isEffectivelyCrit = node.IsCrit || (node.IsBigHit && ch.BigHitActsAsCrit);

                                if (ch.AcceptsIncomingDamage && !isEffectivelyCrit)
                                    baseColor = plugin.Configuration.ColorZone2;
                                else if (isEffectivelyCrit)
                                    baseColor = plugin.Configuration.ColorZone1Crit;

                                if (ch.ColorizeByType)
                                {
                                    if (node.DmgType == 2) baseColor = plugin.Configuration.ColorMagical;
                                    else if (node.DmgType == 3) baseColor = plugin.Configuration.ColorUnique;
                                    else baseColor = plugin.Configuration.ColorPhysical;
                                }

                                if (node.IsBigHit && ch.ColorizeBigHit)
                                {
                                    baseColor = plugin.Configuration.ColorBigHit;
                                }
                            }

                            baseColor.W = alpha; uint color = ImGui.ColorConvertFloat4ToU32(baseColor);
                            uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(plugin.Configuration.ColorOutline.X, plugin.Configuration.ColorOutline.Y, plugin.Configuration.ColorOutline.Z, alpha));

                            float currentScale = ch.NormalScale;
                            if (ch.Mode == ChannelMode.Scrolling && !node.IsAlert && !node.IsTextOnly)
                            {
                                if (node.IsBigHit)
                                {
                                    currentScale = ch.BigHitScale;
                                }
                                else if (node.IsCrit && node.IsDirectHit) currentScale = ch.CritScale * 1.3f;
                                else if (node.IsCrit) currentScale = ch.CritScale;
                                else if (node.IsDirectHit) currentScale = ch.NormalScale + ((ch.CritScale - ch.NormalScale) * 0.5f);
                            }

                            if (ch.Mode == ChannelMode.Scrolling && node.Timer < 0.15f && !node.IsAlert && !node.IsTextOnly)
                            {
                                float popMultiplier = 0.35f;
                                if (node.IsBigHit) popMultiplier = 0.9f;
                                else if (node.IsCrit && node.IsDirectHit) popMultiplier = 0.9f;
                                else if (node.IsCrit) popMultiplier = 0.6f;

                                if (ch.Direction == ScrollDirection.Pop && !node.IsCrit && !node.IsDirectHit && !node.IsBigHit) popMultiplier = 0.8f;
                                currentScale *= (1.0f + (1.0f - (1f - (float)Math.Pow(1f - (node.Timer / 0.15f), 3))) * popMultiplier);
                            }

                            if (ch.PulseEffect) currentScale += (float)Math.Sin(node.Timer * Math.PI * ch.PulseSpeed) * ch.PulseAmplitude;

                            if (alpha <= 0.01f || float.IsNaN(currentScale) || float.IsInfinity(currentScale)) return;

                            IDisposable? fontPusher = null;
                            try
                            {
                                var font = plugin.FontManager.GetChannelFont(ch.FontFileName);
                                if (font != null && font.Available) fontPusher = font.Push();

                                var iconWrap = GetIconWrap(node.IconId);
                                bool hasIconHandle = iconWrap != null;
                                ImTextureID iconHandle = hasIconHandle ? iconWrap!.Handle : default;

                                float aspect = (hasIconHandle && iconWrap!.Height > 0) ? ((float)iconWrap.Width / iconWrap.Height) : 1.0f;
                                bool isSquare = Math.Abs(aspect - 1.0f) < 0.05f;

                                string safeNodeText = node.Text ?? "";

                                if (ch.Mode == ChannelMode.Overlay)
                                {
                                    float iconH = 80.0f * currentScale * ch.IconScale;
                                    float iconW = iconH * aspect;

                                    Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY - (iconH / 2f));
                                    if (ch.Alignment == TextAlignment.Center) drawPos.X -= iconW / 2f;
                                    else if (ch.Alignment == TextAlignment.Right) drawPos.X -= iconW;

                                    Vector2 pMax = drawPos + new Vector2(iconW, iconH);

                                    if (hasIconHandle)
                                    {
                                        drawList.AddImageRounded(iconHandle, drawPos, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 8f);
                                        if (isSquare) drawList.AddRect(drawPos, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 8f, ImDrawFlags.None, 2f);
                                        drawList.AddRect(drawPos, pMax, outlineColor, 8f, ImDrawFlags.None, 2f);

                                        if (node.MaxDuration > 0.01f)
                                        {
                                            float radialProgress = Math.Clamp(1.0f - (node.RemainingTime / node.MaxDuration), 0f, 1f);
                                            Vector2 center = drawPos + new Vector2(iconW / 2f, iconH / 2f);
                                            uint dialColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f * alpha));

                                            ImGui.PushClipRect(drawPos, pMax, true);
                                            DrawRadialCooldown(drawList, center, Math.Max(iconW, iconH), radialProgress, dialColor);
                                            ImGui.PopClipRect();

                                            string timerText = node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m" : $"{(int)node.RemainingTime}";
                                            float timerScale = currentScale * 1.5f; ImGui.SetWindowFontScale(timerScale);
                                            DrawTextWithOutline(drawList, new Vector2(center.X - (ImGui.CalcTextSize(timerText).X / 2f), center.Y - (ImGui.CalcTextSize(timerText).Y / 2f)), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)), outlineColor, timerScale);
                                        }
                                    }
                                }
                                else if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.ProgressBar)
                                {
                                    string safeSkillName = string.IsNullOrEmpty(node.SkillName) ? (node.BaseText ?? "") : node.SkillName;
                                    safeSkillName ??= "";

                                    string timerText = node.MaxDuration > 0.01f ? (node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m {(int)(node.RemainingTime % 60)}s" : $"{node.RemainingTime:F1}s") : "";

                                    ImGui.SetWindowFontScale(currentScale * 0.8f);
                                    float nameW = ImGui.CalcTextSize(safeSkillName).X;
                                    float timerW = timerText.Length > 0 ? ImGui.CalcTextSize(timerText).X : 0f;
                                    float estimatedTimerW = timerText.Length > 0 ? ImGui.CalcTextSize("00.0s").X : 0f;
                                    ImGui.SetWindowFontScale(currentScale);

                                    float barH = 26.0f * currentScale;
                                    bool hasIcon = hasIconHandle && !ch.HideIcons;

                                    float iconH = hasIcon ? barH : 0f;
                                    float iconW = iconH * aspect;

                                    float barW = Math.Max(150.0f * currentScale, iconW + nameW + Math.Max(timerW, estimatedTimerW) + (20f * currentScale));

                                    Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);
                                    if (ch.Alignment == TextAlignment.Center) drawPos.X -= barW / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= barW;

                                    if (plugin.Configuration.DebugShowIds && node.StatusId > 0) { ImGui.SetWindowFontScale(currentScale * 0.6f); DrawTextWithOutline(drawList, new Vector2(drawPos.X, drawPos.Y - 14f * currentScale), $"[ID: {node.StatusId}]", ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, alpha)), outlineColor, currentScale * 0.6f); }

                                    float maxH = Math.Max(barH, iconH);
                                    float barY = drawPos.Y + (maxH - barH) / 2f;
                                    float iconY = drawPos.Y + (maxH - iconH) / 2f;

                                    drawList.AddRectFilled(new Vector2(drawPos.X, barY), new Vector2(drawPos.X + barW, barY + barH), ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.7f * alpha)), 4f);
                                    if (node.MaxDuration > 0.01f) drawList.AddRectFilled(new Vector2(drawPos.X, barY), new Vector2(drawPos.X + (barW * Math.Clamp(node.RemainingTime / node.MaxDuration, 0f, 1f)), barY + barH), color, 4f);

                                    if (hasIcon)
                                    {
                                        Vector2 pMin = ch.IconOnRight ? new Vector2(drawPos.X + barW - iconW, iconY) : new Vector2(drawPos.X, iconY);
                                        Vector2 pMax = ch.IconOnRight ? new Vector2(drawPos.X + barW, iconY + iconH) : new Vector2(drawPos.X + iconW, iconY + iconH);

                                        drawList.AddImageRounded(iconHandle, pMin, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 4f);
                                        if (isSquare) drawList.AddRect(pMin, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 4f, ImDrawFlags.None, 1.5f);
                                    }

                                    drawList.AddRect(new Vector2(drawPos.X, barY), new Vector2(drawPos.X + barW, barY + barH), outlineColor, 4f, ImDrawFlags.None, 1.5f);

                                    ImGui.SetWindowFontScale(currentScale * 0.8f);
                                    float tY = barY + (barH / 2f) - (ImGui.GetFontSize() / 2f);

                                    if (ch.IconOnRight)
                                    {
                                        float nameRightX = drawPos.X + barW - iconW - (5f * currentScale) - nameW;
                                        DrawTextWithOutline(drawList, new Vector2(nameRightX, tY), safeSkillName, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);

                                        if (node.MaxDuration > 0.01f)
                                        {
                                            DrawTextWithOutline(drawList, new Vector2(drawPos.X + (5f * currentScale), tY), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);
                                        }
                                    }
                                    else
                                    {
                                        float textOffsetX = iconW + (5f * currentScale);
                                        DrawTextWithOutline(drawList, new Vector2(drawPos.X + textOffsetX, tY), safeSkillName, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);

                                        if (node.MaxDuration > 0.01f)
                                        {
                                            DrawTextWithOutline(drawList, new Vector2(drawPos.X + barW - timerW - (5f * currentScale), tY), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);
                                        }
                                    }
                                }
                                else if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle != TrackerStyle.Text)
                                {
                                    float iconH = 40.0f * currentScale * ch.IconScale;
                                    float iconW = iconH * aspect;
                                    Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);
                                    if (ch.Alignment == TextAlignment.Center) drawPos.X -= iconW / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= iconW;

                                    if (plugin.Configuration.DebugShowIds && node.StatusId > 0) { ImGui.SetWindowFontScale(currentScale * 0.6f); DrawTextWithOutline(drawList, new Vector2(drawPos.X, drawPos.Y - 14f * currentScale), $"[ID: {node.StatusId}]", ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, alpha)), outlineColor, currentScale * 0.6f); }

                                    if (hasIconHandle)
                                    {
                                        Vector2 pMax = drawPos + new Vector2(iconW, iconH);
                                        drawList.AddImageRounded(iconHandle, drawPos, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 4f);
                                        if (isSquare) drawList.AddRect(drawPos, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 4f, ImDrawFlags.None, 2f);

                                        if (ch.TrackerStyle == TrackerStyle.IconDial && node.MaxDuration > 0.01f)
                                        {
                                            ImGui.PushClipRect(drawPos, pMax, true);
                                            DrawRadialCooldown(drawList, drawPos + new Vector2(iconW / 2f, iconH / 2f), Math.Max(iconW, iconH), Math.Clamp(1.0f - (node.RemainingTime / node.MaxDuration), 0f, 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f * alpha)));
                                            ImGui.PopClipRect();
                                        }

                                        if (node.RemainingTime > 0)
                                        {
                                            string timerText = node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m" : $"{(int)node.RemainingTime}";
                                            float timerScale = currentScale * ch.TrackerTimerScale; ImGui.SetWindowFontScale(timerScale);
                                            Vector2 textPos = ch.TrackerStyle == TrackerStyle.IconDial ? new Vector2(drawPos.X + (iconW / 2f) - (ImGui.CalcTextSize(timerText).X / 2f), drawPos.Y + (iconH / 2f) - (ImGui.CalcTextSize(timerText).Y / 2f)) : new Vector2(drawPos.X + (iconW / 2f) - (ImGui.CalcTextSize(timerText).X / 2f), drawPos.Y + iconH + 2f);
                                            DrawTextWithOutline(drawList, textPos, timerText, color, outlineColor, timerScale);
                                        }
                                    }
                                }
                                else if (ch.Mode == ChannelMode.Scrolling || (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.Text))
                                {
                                    ImGui.SetWindowFontScale(currentScale);
                                    bool hasIcon = hasIconHandle && !ch.HideIcons;
                                    float iconH = hasIcon ? (28.0f * currentScale * ch.IconScale) : 0f;
                                    float iconW = iconH * aspect;
                                    float padding = hasIcon && !string.IsNullOrEmpty(safeNodeText) ? 6.0f : 0f; float textWidth = ImGui.CalcTextSize(safeNodeText).X;
                                    float totalWidth = textWidth + iconW + padding; Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);

                                    if (ch.Alignment == TextAlignment.Center) drawPos.X -= totalWidth / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= totalWidth;

                                    if (ch.IconOnRight)
                                    {
                                        DrawTextWithOutline(drawList, drawPos, safeNodeText, color, outlineColor, currentScale);
                                        if (hasIcon)
                                        {
                                            Vector2 pMin = new Vector2(drawPos.X + textWidth + padding, drawPos.Y - ((iconH - ImGui.GetFontSize()) / 2f));
                                            Vector2 pMax = new Vector2(pMin.X + iconW, pMin.Y + iconH);
                                            drawList.AddImageRounded(iconHandle, pMin, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 3f);
                                            if (isSquare) drawList.AddRect(pMin, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 3f, ImDrawFlags.None, 1.5f);
                                        }
                                    }
                                    else
                                    {
                                        if (hasIcon)
                                        {
                                            Vector2 pMin = new Vector2(drawPos.X, drawPos.Y - ((iconH - ImGui.GetFontSize()) / 2f));
                                            Vector2 pMax = new Vector2(pMin.X + iconW, pMin.Y + iconH);
                                            drawList.AddImageRounded(iconHandle, pMin, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 3f);
                                            if (isSquare) drawList.AddRect(pMin, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 3f, ImDrawFlags.None, 1.5f);
                                            drawPos.X += iconW + padding;
                                        }
                                        DrawTextWithOutline(drawList, drawPos, safeNodeText, color, outlineColor, currentScale);
                                    }
                                }
                            }
                            finally { ImGui.SetWindowFontScale(1.0f); fontPusher?.Dispose(); }
                        }

                        foreach (var node in plugin.CustomTexts) { if (node.IsActive && !node.IsCrit) DrawNodeItem(node); }
                        foreach (var node in plugin.CustomTexts) { if (node.IsActive && node.IsCrit) DrawNodeItem(node); }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error(ex, "Crash prevented in MSBT Tracker Rendering!");
                    }
                    finally
                    {
                        ImGui.End();
                    }
                }
            }
        }
    }

    public void SpawnTestText(bool isCrit, DisplayChannel ch, bool isHeal = false, bool isAlert = false)
    {
        bool isDirectHit = isCrit;
        string marks = (isCrit && isDirectHit) ? "!!" : (isCrit ? "!" : (isDirectHit ? "*" : ""));
        string txt = ch.IconOnRight ? (marks + "9999") : ("9999" + marks);
        string name = "Test Test Test";

        if (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay)
        {
            txt = "Test Status";
            if (ch.ShowStatusPrefixes && !ch.HideSkillNames) txt = ch.IconOnRight ? txt + " +" : "+ " + txt;
            if (ch.Mode != ChannelMode.Tracker && ch.ShowStatusDuration && !ch.HideSkillNames) txt = ch.IconOnRight ? "(15s) " + txt : txt + " (15s)";
        }
        else if (!ch.HideSkillNames) { txt = ch.IconOnRight ? $"{txt} {name}" : $"{name} {txt}"; }

        uint fakeIcon = 15004; if (ch.HideIcons && ch.TrackerStyle == TrackerStyle.Text) fakeIcon = 0;
        if (isCrit && ch.CritSound > 0) PlayInGameSound(ch.CritSound);
        if (isAlert && ch.AlertSound > 0) PlayInGameSound(ch.AlertSound);

        lock (plugin.CustomTexts)
        {
            int critBehavior = (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay) ? 0 : ch.CritBehavior;
            bool treatAsCrit = isCrit || ch.BigHitActsAsCrit;
            bool isCritStream = treatAsCrit && critBehavior != 0 && !isAlert && ch.Mode == ChannelMode.Scrolling;

            float spawnOffset = GetSpawnOffsetAndBump(ch, isCritStream ? ch.CritScale : ch.NormalScale, ch.Direction, isCritStream);

            var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
            if (node == null) { node = new CustomSCTNode(); plugin.CustomTexts.Add(node); }

            node.Init(txt, txt, 0f, spawnOffset, isCrit, isDirectHit, isHeal, (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay), false, isAlert, ch, fakeIcon, 9999, uint.MaxValue, name, false, 0, 0, 10f, 10f, 1);
            node.DistanceTraveled = 0f;
        }
    }

    public void SpawnIpcAlert(string text, DisplayChannel ch, int soundId)
    {
        if (soundId > 0) PlayInGameSound(soundId);
        else if (ch.AlertSound > 0) PlayInGameSound(ch.AlertSound);

        lock (plugin.CustomTexts)
        {
            float spawnOffset = GetSpawnOffsetAndBump(ch, ch.NormalScale, ch.Direction, false);

            var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
            if (node == null) { node = new CustomSCTNode(); plugin.CustomTexts.Add(node); }

            node.Init(text ?? "", text ?? "", 0f, spawnOffset, false, false, false, true, false, true, ch, 0, 0, uint.MaxValue, "", false, 0, 0, 10f, 10f, 0);
            node.DistanceTraveled = 0f;
        }
    }
}
