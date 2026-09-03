using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

internal sealed partial class ConfigWindow
{
    private void DrawScrollingChannelsTab()
    {
        using var tab = ImRaii.TabItem("Dynamic Channels (Damage / Alerts)");
        if (tab)
        {
            ImGui.Spacing();

            using (var listChild = ImRaii.Child("ScrollChannelsList", new Vector2(220, 0), true))
            {
                if (listChild)
                {
                    if (ImGui.Button("+ Add Channel", new Vector2(-1, 30)))
                    {
                        configuration.Channels.Add(new DisplayChannel { Name = $"Scrolling Text {configuration.Channels.Count + 1}", Mode = ChannelMode.Scrolling });
                        configuration.Save();
                    }
                    ImGui.Separator();

                    for (int i = 0; i < configuration.Channels.Count; i++)
                    {
                        var ch = configuration.Channels[i];
                        if (ch.Mode != ChannelMode.Scrolling) continue;

                        string label = $"{(ch.Enabled ? "[ON]" : "[OFF]")} {ch.Name}##s{i}";
                        if (ImGui.Selectable(label, selectedScrollChannelIndex == i)) selectedScrollChannelIndex = i;
                    }
                }
            }

            ImGui.SameLine();

            using (var settingsChild = ImRaii.Child("ScrollChannelSettings", new Vector2(0, 0), false))
            {
                if (settingsChild)
                {
                    if (selectedScrollChannelIndex >= 0 && selectedScrollChannelIndex < configuration.Channels.Count && configuration.Channels[selectedScrollChannelIndex].Mode == ChannelMode.Scrolling)
                    {
                        DrawChannelSettings(configuration.Channels[selectedScrollChannelIndex], selectedScrollChannelIndex);
                    }
                }
            }
        }
    }

    private void DrawTrackerChannelsTab()
    {
        using var tab = ImRaii.TabItem("Static Panels (Trackers / Overlays)");
        if (tab)
        {
            ImGui.Spacing();

            using (var listChild = ImRaii.Child("TrackerChannelsList", new Vector2(220, 0), true))
            {
                if (listChild)
                {
                    if (ImGui.Button("+ Add Tracker", new Vector2(-1, 30)))
                    {
                        configuration.Channels.Add(new DisplayChannel { Name = $"New Tracker {configuration.Channels.Count + 1}", Mode = ChannelMode.Tracker, Direction = ScrollDirection.Right, TrackerStyle = TrackerStyle.IconDial });
                        configuration.Save();
                    }
                    if (ImGui.Button("+ Add Overlay", new Vector2(-1, 30)))
                    {
                        configuration.Channels.Add(new DisplayChannel { Name = $"New Overlay {configuration.Channels.Count + 1}", Mode = ChannelMode.Overlay, Direction = ScrollDirection.Static, NormalScale = 2.0f });
                        configuration.Save();
                    }
                    ImGui.Separator();

                    for (int i = 0; i < configuration.Channels.Count; i++)
                    {
                        var ch = configuration.Channels[i];
                        if (ch.Mode == ChannelMode.Scrolling) continue;

                        string icon = ch.Mode == ChannelMode.Overlay ? "[Ovl]" : "[Trk]";
                        string label = $"{(ch.Enabled ? "[ON]" : "[OFF]")} {icon} {ch.Name}##t{i}";
                        if (ImGui.Selectable(label, selectedTrackerChannelIndex == i)) selectedTrackerChannelIndex = i;
                    }
                }
            }

            ImGui.SameLine();

            using (var settingsChild = ImRaii.Child("TrackerChannelSettings", new Vector2(0, 0), false))
            {
                if (settingsChild)
                {
                    if (selectedTrackerChannelIndex >= 0 && selectedTrackerChannelIndex < configuration.Channels.Count && configuration.Channels[selectedTrackerChannelIndex].Mode != ChannelMode.Scrolling)
                    {
                        DrawChannelSettings(configuration.Channels[selectedTrackerChannelIndex], selectedTrackerChannelIndex, configuration.Channels[selectedTrackerChannelIndex].Mode);
                    }
                }
            }
        }
    }

    private void DrawChannelSettings(DisplayChannel ch, int index, ChannelMode mode = ChannelMode.Scrolling)
    {
        bool changed = false;
        bool isTracker = mode == ChannelMode.Tracker;
        bool isOverlay = mode == ChannelMode.Overlay;

        using (ImRaii.ItemWidth(250f))
        {
            string chName = ch.Name;
            if (ImGui.InputText("Channel Name", ref chName, 50)) { ch.Name = chName; changed = true; }
        }

        ImGui.SameLine(ImGui.GetWindowWidth() - 100);
        if (ImGui.Button("Delete", new Vector2(80, 25))) { configuration.Channels.RemoveAt(index); configuration.Save(); return; }

        bool enabled = ch.Enabled;
        if (ImGui.Checkbox("Enable Channel", ref enabled)) { ch.Enabled = enabled; changed = true; }

        ImGui.SameLine(); ImGui.Dummy(new Vector2(20, 0)); ImGui.SameLine();
        if (ImGui.Button("Test", new Vector2(80, 25))) plugin.SpawnTestText(false, ch);
        if (mode == ChannelMode.Scrolling) { ImGui.SameLine(); if (ImGui.Button("Test (Crit)", new Vector2(100, 25))) plugin.SpawnTestText(true, ch); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        using var chTabs = ImRaii.TabBar("ChannelSettingsSubTabs");
        if (chTabs)
        {
            using (var tabFilters = ImRaii.TabItem("Filters & Sources"))
            {
                if (tabFilters)
                {
                    ImGui.Spacing();
                    if (isOverlay)
                    {
                        ImGui.TextColored(new Vector4(0.8f, 0.4f, 1f, 1f), "The Overlay ONLY displays Smart Triggers (Auras).");
                        ImGui.Text("The overlay appears automatically by tracking your buffs.");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.8f, 0.4f, 1f, 1f), "What does this channel show?");
                        ImGui.Indent(10f);

                        if (!isTracker)
                        {
                            bool cOut = ch.AcceptsOutgoingDamage; if (ImGui.Checkbox("My Outgoing Damage", ref cOut)) { ch.AcceptsOutgoingDamage = cOut; changed = true; }
                            ImGui.SameLine(250f);
                            bool cInc = ch.AcceptsIncomingDamage; if (ImGui.Checkbox("Incoming Damage", ref cInc)) { ch.AcceptsIncomingDamage = cInc; changed = true; }
                            bool cOutHeal = ch.AcceptsOutgoingHeals; if (ImGui.Checkbox("My Outgoing Healing", ref cOutHeal)) { ch.AcceptsOutgoingHeals = cOutHeal; changed = true; }
                            ImGui.SameLine(250f);
                            bool cHeal = ch.AcceptsHeals; if (ImGui.Checkbox("Incoming Healing", ref cHeal)) { ch.AcceptsHeals = cHeal; changed = true; }
                            bool cMp = ch.AcceptsMp; if (ImGui.Checkbox("Incoming Mana (MP/CP/GP)", ref cMp)) { ch.AcceptsMp = cMp; changed = true; }
                            ImGui.SameLine(250f);
                        }

                        bool cStat = ch.AcceptsStatuses; if (ImGui.Checkbox("Buffs/Debuffs on Me", ref cStat)) { ch.AcceptsStatuses = cStat; changed = true; }
                        if (isTracker) ImGui.SameLine(250f);

                        bool cOutStat = ch.AcceptsOutgoingStatuses; if (ImGui.Checkbox("My Statuses/Debuffs on Target", ref cOutStat)) { ch.AcceptsOutgoingStatuses = cOutStat; changed = true; }
                        if (!isTracker) ImGui.SameLine(250f);

                        bool cAlert = ch.AcceptsSystemAlerts; if (ImGui.Checkbox("System Alerts", ref cAlert)) { ch.AcceptsSystemAlerts = cAlert; changed = true; }

                        if (!isTracker)
                        {
                            bool cColorType = ch.ColorizeByType;
                            if (ImGui.Checkbox("Colorize Damage by Type (Phys/Mag/Unique)", ref cColorType)) { ch.ColorizeByType = cColorType; changed = true; }

                            bool cColorBigHit = ch.ColorizeBigHit;
                            if (ImGui.Checkbox("Colorize Big Hits (Custom Big Hit Color)", ref cColorBigHit)) { ch.ColorizeBigHit = cColorBigHit; changed = true; }
                        }

                        ImGui.Spacing();
                        bool cTargetOnly = ch.CurrentTargetOnly;
                        if (ImGui.Checkbox("Show only on current target (Ignore AoE on other mobs)", ref cTargetOnly)) { ch.CurrentTargetOnly = cTargetOnly; changed = true; }
                        ImGui.Unindent(10f);
                    }
                }
            }

            using (var tabLayout = ImRaii.TabItem("Layout & Scales"))
            {
                if (tabLayout)
                {
                    ImGui.Spacing();
                    using (ImRaii.ItemWidth(160f))
                    {

                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Position & Size");
                        ImGui.Indent(10f);
                        float cX = ch.X; if (ImGui.DragFloat("X Axis", ref cX, 1f)) { ch.X = cX; changed = true; }
                        float cY = ch.Y; if (ImGui.DragFloat("Y Axis", ref cY, 1f)) { ch.Y = cY; changed = true; }

                        int align = (int)ch.Alignment;
                        if (ImGui.Combo("Anchor Alignment", ref align, alignNames, alignNames.Length)) { ch.Alignment = (TextAlignment)align; changed = true; }

                        float cNorm = ch.NormalScale; if (ImGui.DragFloat("Base Scale", ref cNorm, 0.05f, 0.1f, 5.0f)) { ch.NormalScale = cNorm; changed = true; }

                        if (!isTracker && !isOverlay)
                        {
                            float cCrit = ch.CritScale; if (ImGui.DragFloat("Crit Scale", ref cCrit, 0.05f, 0.1f, 10.0f)) { ch.CritScale = cCrit; changed = true; }
                        }

                        float cIcon = ch.IconScale; if (ImGui.DragFloat("Extra Icon Scale", ref cIcon, 0.05f, 0.1f, 5.0f)) { ch.IconScale = cIcon; changed = true; }

                        if (isTracker || isOverlay)
                        {
                            float tTimer = ch.TrackerTimerScale; if (ImGui.DragFloat("Timer Digits Scale", ref tTimer, 0.05f, 0.1f, 5.0f)) { ch.TrackerTimerScale = tTimer; changed = true; }
                        }
                        ImGui.Unindent(10f);

                        if (isTracker || isOverlay)
                        {
                            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Tracker Settings");
                            ImGui.Indent(10f);
                            int tStyle = (int)ch.TrackerStyle;
                            if (ImGui.Combo("Display Style", ref tStyle, trackerStyleNames, trackerStyleNames.Length)) { ch.TrackerStyle = (TrackerStyle)tStyle; changed = true; }

                            if (isTracker)
                            {
                                int tDir = ch.Direction == ScrollDirection.Up ? 0 : (ch.Direction == ScrollDirection.Down ? 1 : (ch.Direction == ScrollDirection.Left ? 2 : 3));
                                if (ImGui.Combo("List Direction", ref tDir, TrackerDirections, TrackerDirections.Length))
                                {
                                    ch.Direction = tDir == 0 ? ScrollDirection.Up : (tDir == 1 ? ScrollDirection.Down : (tDir == 2 ? ScrollDirection.Left : ScrollDirection.Right));
                                    changed = true;
                                }
                            }
                            ImGui.Unindent(10f);
                        }
                    }
                }
            }

            if (mode == ChannelMode.Scrolling)
            {
                using (var tabAnim = ImRaii.TabItem("Movement"))
                {
                    if (tabAnim)
                    {
                        ImGui.Spacing();
                        using (ImRaii.ItemWidth(160f))
                        {
                            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Animation Rules");
                            ImGui.Indent(10f);
                            int cDir = (int)ch.Direction; if (ImGui.Combo("Spawn Animation", ref cDir, ScrollDirections, ScrollDirections.Length)) { ch.Direction = (ScrollDirection)cDir; changed = true; }
                            float cCurve = ch.Curve; if (ImGui.DragFloat("Arc Curvature", ref cCurve, 0.5f, -100f, 100f)) { ch.Curve = cCurve; changed = true; }
                            float cSpeed = ch.Speed; if (ImGui.DragFloat("Scroll Speed", ref cSpeed, 1f, 10f, 300f)) { ch.Speed = cSpeed; changed = true; }
                            float cDur = ch.Duration; if (ImGui.DragFloat("Normal Duration (sec)", ref cDur, 0.05f, 0.5f, 10.0f)) { ch.Duration = cDur; changed = true; }
                            float cFade = ch.FadeDuration; if (ImGui.DragFloat("Fade Duration (sec)", ref cFade, 0.05f, 0.0f, 5.0f)) { ch.FadeDuration = cFade; changed = true; }
                            ImGui.Unindent(10f);
                        }
                    }
                }

                using (var tabCrits = ImRaii.TabItem("Crits & Big Hits"))
                {
                    if (tabCrits)
                    {
                        ImGui.Spacing();
                        using (ImRaii.ItemWidth(160f))
                        {
                            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Critical Hit Dynamics");
                            ImGui.Indent(10f);

                            int csound = ch.CritSound; if (ImGui.Combo("Crit Sound", ref csound, SoundOptions, SoundOptions.Length)) { ch.CritSound = csound; changed = true; }

                            int cBehav = ch.CritBehavior; if (ImGui.Combo("Crit Behavior", ref cBehav, critBehaviors, critBehaviors.Length)) { ch.CritBehavior = cBehav; changed = true; }

                            if (cBehav != 0)
                            {
                                float cOx = ch.CritOffsetX; if (ImGui.DragFloat("Spawn Offset X", ref cOx, 1f, -1000f, 1000f)) { ch.CritOffsetX = cOx; changed = true; }
                                float cOy = ch.CritOffsetY; if (ImGui.DragFloat("Spawn Offset Y", ref cOy, 1f, -1000f, 1000f)) { ch.CritOffsetY = cOy; changed = true; }

                                ImGui.Spacing();
                                ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Custom Crit Arc");
                                float cCCurve = ch.CritCurve; if (ImGui.DragFloat("Crit Arc Curvature", ref cCCurve, 0.5f, -100f, 100f)) { ch.CritCurve = cCCurve; changed = true; }
                                float cCStart = ch.CritCurveStart; if (ImGui.DragFloat("Arc Start Clip (0.0 - 1.0)", ref cCStart, 0.05f, 0f, 1f)) { ch.CritCurveStart = cCStart; changed = true; }
                                float cCEnd = ch.CritCurveEnd; if (ImGui.DragFloat("Arc End Clip (0.0 - 1.0)", ref cCEnd, 0.05f, 0f, 1f)) { ch.CritCurveEnd = cCEnd; changed = true; }
                                ImGui.Spacing();

                                if (cBehav == 2)
                                {
                                    float cLing = ch.CritLinger; if (ImGui.DragFloat("Freeze Time (sec)", ref cLing, 0.05f, 0.0f, 10.0f)) { ch.CritLinger = cLing; changed = true; }
                                }

                                float cCDur = ch.CritDuration; if (ImGui.DragFloat("Total Duration (sec)", ref cCDur, 0.05f, 0.5f, 10.0f)) { ch.CritDuration = cCDur; changed = true; }

                                if (cBehav == 2)
                                {
                                    float cPhase = ch.CritCurvePhase; if (ImGui.DragFloat("Merge with Main Stream", ref cPhase, 0.01f, 0f, 1f)) { ch.CritCurvePhase = cPhase; changed = true; }
                                }
                            }
                            ImGui.Unindent(10f);

                            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Big Hit Customization");
                            ImGui.Indent(10f);

                            int bht = ch.BigHitThreshold; if (ImGui.DragInt("Big Hit Threshold (0 = off)", ref bht, 1000, 0, 9999999)) { ch.BigHitThreshold = bht; changed = true; }

                            if (bht > 0)
                            {
                                string bigHitPrefix = ch.BigHitPrefix ?? "";
                                if (ImGui.InputText($"Prefix (Before)##{ch.Name}", ref bigHitPrefix, 16)) { ch.BigHitPrefix = bigHitPrefix; changed = true; }

                                string bigHitSuffix = ch.BigHitSuffix ?? "";
                                if (ImGui.InputText($"Suffix (After)##{ch.Name}", ref bigHitSuffix, 16)) { ch.BigHitSuffix = bigHitSuffix; changed = true; }

                                float bigHitScale = ch.BigHitScale > 0 ? ch.BigHitScale : 1.3f;
                                if (ImGui.DragFloat($"Big Hit Scale##{ch.Name}", ref bigHitScale, 0.05f, 0.5f, 5.0f)) { ch.BigHitScale = bigHitScale; changed = true; }

                                bool actsAsCrit = ch.BigHitActsAsCrit;
                                if (ImGui.Checkbox($"Route to Crit Trajectory (Fly with crits)##{ch.Name}", ref actsAsCrit)) { ch.BigHitActsAsCrit = actsAsCrit; changed = true; }
                            }
                            ImGui.Unindent(10f);

                            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                            ImGui.Indent(10f);
                            int sht = ch.SmallHitThreshold; if (ImGui.DragInt("Treat crits below as Normal (0 = off)", ref sht, 100, 0, 9999999)) { ch.SmallHitThreshold = sht; changed = true; }
                            ImGui.Unindent(10f);
                        }
                    }
                }
            }

            using (var tabVis = ImRaii.TabItem("Text & Visuals"))
            {
                if (tabVis)
                {
                    ImGui.Spacing();
                    string fontName = ch.FontKey ?? "";
                    using (ImRaii.ItemWidth(250f))
                    {
                        if (DrawFontSelector("Font", ref fontName, true)) { ch.FontKey = fontName; changed = true; }
                    }
                    DrawHelpMarker("Choose a registered game or custom font, or inherit the global font.");

                    bool inheritFontSize = ch.FontSize <= 0;
                    if (ImGui.Checkbox("Use Global Font Size", ref inheritFontSize))
                    {
                        ch.FontSize = inheritFontSize ? 0 : configuration.FontSize;
                        changed = true;
                    }
                    if (!inheritFontSize)
                    {
                        float fontSize = ch.FontSize;
                        using (ImRaii.ItemWidth(150f))
                        {
                            if (ImGui.DragFloat("Font Size", ref fontSize, 1f, 8f, 96f, "%.0f px"))
                            {
                                ch.FontSize = fontSize;
                                changed = true;
                            }
                        }
                    }

                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                    if (mode == ChannelMode.Scrolling)
                    {
                        using (ImRaii.ItemWidth(150f))
                        {
                            int spam = ch.SpamThreshold; if (ImGui.DragInt("Hide Damage Below", ref spam, 50, 0, 100000)) { ch.SpamThreshold = spam; changed = true; }
                        }
                    }

                    if (!isOverlay)
                    {
                        bool hName = ch.HideSkillNames; if (ImGui.Checkbox("Hide Skill Names", ref hName)) { ch.HideSkillNames = hName; changed = true; }
                    }

                    if (ch.TrackerStyle != TrackerStyle.Text && isTracker) { ch.HideIcons = false; }
                    else
                    {
                        bool hIcon = ch.HideIcons; if (ImGui.Checkbox("Hide Icons", ref hIcon)) { ch.HideIcons = hIcon; changed = true; }
                        if (!hIcon) { ImGui.SameLine(250f); bool iconRight = ch.IconOnRight; if (ImGui.Checkbox("Icon on Right (RTL)", ref iconRight)) { ch.IconOnRight = iconRight; changed = true; } }
                    }

                    if (mode != ChannelMode.Scrolling)
                    {
                        if (ch.TrackerStyle == TrackerStyle.Text)
                        {
                            bool sDur = ch.ShowStatusDuration; if (ImGui.Checkbox("Show Buff Duration", ref sDur)) { ch.ShowStatusDuration = sDur; changed = true; }
                        }
                    }
                    else
                    {
                        bool sPrefix = ch.ShowStatusPrefixes; if (ImGui.Checkbox("Show Status Prefixes (+ / -)", ref sPrefix)) { ch.ShowStatusPrefixes = sPrefix; changed = true; }
                        bool sDur = ch.ShowStatusDuration; if (ImGui.Checkbox("Show Buff Duration", ref sDur)) { ch.ShowStatusDuration = sDur; changed = true; }
                    }

                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                    bool abb = ch.AbbreviateSkillNames; if (ImGui.Checkbox("Abbreviate Long Names", ref abb)) { ch.AbbreviateSkillNames = abb; changed = true; }
                    if (abb)
                    {
                        ImGui.SameLine();
                        using (ImRaii.ItemWidth(100f))
                        {
                            int maxL = ch.MaxSkillNameLength; if (ImGui.DragInt("Max Chars", ref maxL, 1f, 5, 40)) { ch.MaxSkillNameLength = maxL; changed = true; }
                        }
                    }

                    bool sAbs = ch.ShowAbsorbs; if (ImGui.Checkbox("Show Full Absorbs (Damage = 0)", ref sAbs)) { ch.ShowAbsorbs = sAbs; changed = true; }
                    if (sAbs)
                    {
                        ImGui.SameLine();
                        using (ImRaii.ItemWidth(150f))
                        {
                            string absT = ch.AbsorbText; if (ImGui.InputText("Absorb Text", ref absT, 30)) { ch.AbsorbText = absT; changed = true; }
                        }
                    }

                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                    bool sPulse = ch.PulseEffect; if (ImGui.Checkbox("Text Pulse (Scale Animation)", ref sPulse)) { ch.PulseEffect = sPulse; changed = true; }
                    if (sPulse)
                    {
                        ImGui.Indent(10f);
                        using (ImRaii.ItemWidth(150f))
                        {
                            float pSpd = ch.PulseSpeed; if (ImGui.DragFloat("Pulse Speed", ref pSpd, 0.1f, 0.5f, 20.0f)) { ch.PulseSpeed = pSpd; changed = true; }
                            float pAmp = ch.PulseAmplitude; if (ImGui.DragFloat("Pulse Amplitude", ref pAmp, 0.01f, 0.01f, 1.0f)) { ch.PulseAmplitude = pAmp; changed = true; }
                        }
                        ImGui.Unindent(10f);
                    }

                    if (mode == ChannelMode.Scrolling)
                    {
                        ImGui.Spacing();
                        using (ImRaii.ItemWidth(250f))
                        {
                            int asound = ch.AlertSound; if (ImGui.Combo("Sound on Appear (Alerts)", ref asound, SoundOptions, SoundOptions.Length)) { ch.AlertSound = asound; changed = true; }
                        }
                    }
                }
            }
        }
        if (changed) configuration.Save();
    }
}
