using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace MSBT;

internal sealed partial class Renderer
{
    private void DrawNodeItem(CustomSCTNode node, float fontScaleCorrection, ImDrawListPtr drawList, Vector2 viewportSize)
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
            float popProgress = 1f - (node.Timer / 0.15f);
            currentScale *= 1.0f + (popProgress * popProgress * popProgress * popMultiplier);
        }

        if (ch.PulseEffect) currentScale += MathF.Sin(node.Timer * MathF.PI * ch.PulseSpeed) * ch.PulseAmplitude;

        if (alpha <= 0.01f || float.IsNaN(currentScale) || float.IsInfinity(currentScale)) return;
        if (node.CurrentX < -500f || node.CurrentY < -500f ||
            node.CurrentX > viewportSize.X + 500f || node.CurrentY > viewportSize.Y + 500f) return;

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

                    using (new ImGuiClipRectScope(drawPos, pMax, true))
                        DrawHelper.DrawRadialCooldown(drawList, center, Math.Max(iconW, iconH), radialProgress, dialColor);

                    string timerText = node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m" : $"{(int)node.RemainingTime}";
                    float timerScale = currentScale * 1.5f; ImGui.SetWindowFontScale(timerScale * fontScaleCorrection);
                    Vector2 timerSize = ImGui.CalcTextSize(timerText);
                    drawHelper.DrawText(drawList, center - (timerSize / 2f), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha)), outlineColor, timerScale);
                }
            }
        }
        else if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.ProgressBar)
        {
            string safeSkillName = string.IsNullOrEmpty(node.SkillName) ? (node.BaseText ?? "") : node.SkillName;
            safeSkillName ??= "";

            string timerText = node.MaxDuration > 0.01f ? (node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m {(int)(node.RemainingTime % 60)}s" : $"{node.RemainingTime:F1}s") : "";

            ImGui.SetWindowFontScale(currentScale * 0.8f * fontScaleCorrection);
            float nameW = ImGui.CalcTextSize(safeSkillName).X;
            float timerW = timerText.Length > 0 ? ImGui.CalcTextSize(timerText).X : 0f;
            float estimatedTimerW = timerText.Length > 0 ? ImGui.CalcTextSize("00.0s").X : 0f;
            ImGui.SetWindowFontScale(currentScale * fontScaleCorrection);

            float barH = 26.0f * currentScale;
            bool hasIcon = hasIconHandle && !ch.HideIcons;

            float iconH = hasIcon ? barH : 0f;
            float iconW = iconH * aspect;

            float barW = Math.Max(150.0f * currentScale, iconW + nameW + Math.Max(timerW, estimatedTimerW) + (20f * currentScale));

            Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);
            if (ch.Alignment == TextAlignment.Center) drawPos.X -= barW / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= barW;

            if (plugin.Configuration.DebugShowIds && node.StatusId > 0) { ImGui.SetWindowFontScale(currentScale * 0.6f * fontScaleCorrection); drawHelper.DrawText(drawList, new Vector2(drawPos.X, drawPos.Y - 14f * currentScale), $"[ID: {node.StatusId}]", ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, alpha)), outlineColor, currentScale * 0.6f); }

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

            ImGui.SetWindowFontScale(currentScale * 0.8f * fontScaleCorrection);
            float tY = barY + (barH / 2f) - (ImGui.GetFontSize() / 2f);

            if (ch.IconOnRight)
            {
                float nameRightX = drawPos.X + barW - iconW - (5f * currentScale) - nameW;
                drawHelper.DrawText(drawList, new Vector2(nameRightX, tY), safeSkillName, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);

                if (node.MaxDuration > 0.01f)
                {
                    drawHelper.DrawText(drawList, new Vector2(drawPos.X + (5f * currentScale), tY), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);
                }
            }
            else
            {
                float textOffsetX = iconW + (5f * currentScale);
                drawHelper.DrawText(drawList, new Vector2(drawPos.X + textOffsetX, tY), safeSkillName, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);

                if (node.MaxDuration > 0.01f)
                {
                    drawHelper.DrawText(drawList, new Vector2(drawPos.X + barW - timerW - (5f * currentScale), tY), timerText, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), outlineColor, currentScale * 0.8f);
                }
            }
        }
        else if (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle != TrackerStyle.Text)
        {
            float iconH = 40.0f * currentScale * ch.IconScale;
            float iconW = iconH * aspect;
            Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);
            if (ch.Alignment == TextAlignment.Center) drawPos.X -= iconW / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= iconW;

            if (plugin.Configuration.DebugShowIds && node.StatusId > 0) { ImGui.SetWindowFontScale(currentScale * 0.6f * fontScaleCorrection); drawHelper.DrawText(drawList, new Vector2(drawPos.X, drawPos.Y - 14f * currentScale), $"[ID: {node.StatusId}]", ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 1f, alpha)), outlineColor, currentScale * 0.6f); }

            if (hasIconHandle)
            {
                Vector2 pMax = drawPos + new Vector2(iconW, iconH);
                drawList.AddImageRounded(iconHandle, drawPos, pMax, new Vector2(0, 0), new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), 4f);
                if (isSquare) drawList.AddRect(drawPos, pMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), 4f, ImDrawFlags.None, 2f);

                if (ch.TrackerStyle == TrackerStyle.IconDial && node.MaxDuration > 0.01f)
                {
                    using (new ImGuiClipRectScope(drawPos, pMax, true))
                        DrawHelper.DrawRadialCooldown(drawList, drawPos + new Vector2(iconW / 2f, iconH / 2f), Math.Max(iconW, iconH), Math.Clamp(1.0f - (node.RemainingTime / node.MaxDuration), 0f, 1f), ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.7f * alpha)));
                }

                if (node.RemainingTime > 0)
                {
                    string timerText = node.RemainingTime >= 60f ? $"{(int)(node.RemainingTime / 60)}m" : $"{(int)node.RemainingTime}";
                    float timerScale = currentScale * ch.TrackerTimerScale; ImGui.SetWindowFontScale(timerScale * fontScaleCorrection);
                    Vector2 timerSize = ImGui.CalcTextSize(timerText);
                    Vector2 textPos = ch.TrackerStyle == TrackerStyle.IconDial
                        ? drawPos + new Vector2((iconW - timerSize.X) / 2f, (iconH - timerSize.Y) / 2f)
                        : new Vector2(drawPos.X + ((iconW - timerSize.X) / 2f), drawPos.Y + iconH + 2f);
                    drawHelper.DrawText(drawList, textPos, timerText, color, outlineColor, timerScale);
                }
            }
        }
        else if (ch.Mode == ChannelMode.Scrolling || (ch.Mode == ChannelMode.Tracker && ch.TrackerStyle == TrackerStyle.Text))
        {
            ImGui.SetWindowFontScale(currentScale * fontScaleCorrection);
            bool hasIcon = hasIconHandle && !ch.HideIcons;
            float iconH = hasIcon ? (28.0f * currentScale * ch.IconScale) : 0f;
            float iconW = iconH * aspect;
            float padding = hasIcon && !string.IsNullOrEmpty(safeNodeText) ? 6.0f : 0f; float textWidth = ImGui.CalcTextSize(safeNodeText).X;
            float totalWidth = textWidth + iconW + padding; Vector2 drawPos = new Vector2(node.CurrentX, node.CurrentY);

            if (ch.Alignment == TextAlignment.Center) drawPos.X -= totalWidth / 2f; else if (ch.Alignment == TextAlignment.Right) drawPos.X -= totalWidth;

            if (ch.IconOnRight)
            {
                drawHelper.DrawText(drawList, drawPos, safeNodeText, color, outlineColor, currentScale);
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
                drawHelper.DrawText(drawList, drawPos, safeNodeText, color, outlineColor, currentScale);
            }
        }
    }

}
