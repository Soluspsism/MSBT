using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Cfg;
    private Plugin Plugin;
    private readonly string[] critBehaviors = { "In main stream (Fly with others)", "Static burst on side (Only bumped)", "Convergence (Freeze on side, merge into stream)" };
    private readonly string[] alignNames = { "Left", "Center", "Right" };
    private readonly string[] trackerStyleNames = { "Text + Timer", "Icon + Digits Below", "Icon + Radial Dial", "Progress Bar" };

    private readonly string[] conditionTypes = { "No Condition", "My Health (%)", "Target Health (%)", "I HAVE buff (ID)", "I MISS buff (ID)", "Target HAS debuff (ID)", "Target MISSES debuff (ID)", "My buff stacks", "Target buff stacks" };
    private readonly string[] conditionOperators = { "Less Than (<)", "Greater Than (>)", "Equal To (==)" };

    private string importBuffer = "";
    private string newPresetName = "";
    private string auraImportBuffer = "";
    private int selectedScrollChannelIndex = 0;
    private int selectedTrackerChannelIndex = 0;

    private string searchInputBlacklist = "";
    private string searchInputTriggers = "";
    private List<(uint ID, string Name, bool IsStatus)> searchResultsBlacklist = new();
    private List<(uint ID, string Name, bool IsStatus)> searchResultsTriggers = new();

    private readonly (string Group, uint[] Jobs)[] jobGroups = {
        ("Tanks", new uint[] { 19, 21, 32, 37 }),
        ("Healers", new uint[] { 24, 28, 33, 40 }),
        ("Melee DPS", new uint[] { 20, 22, 30, 34, 39, 41 }),
        ("Physical Ranged", new uint[] { 23, 31, 38 }),
        ("Magical Ranged", new uint[] { 25, 27, 35, 36, 42 })
    };

    public ConfigWindow(Plugin plugin) : base(
        "MSBT V3: Configuration",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.Size = new Vector2(800, 850);
        this.Cfg = plugin.Configuration;
        this.Plugin = plugin;
    }

    public void Dispose() { }

    private void DrawHelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(?)");
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
    }

    public override void Draw()
    {
        bool editMode = this.Plugin.IsEditMode;
        if (ImGui.Checkbox("Enable Edit Mode (Grid & Anchors)", ref editMode))
            this.Plugin.IsEditMode = editMode;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using var tabs = ImRaii.TabBar("MSBT_Tabs");
        if (tabs)
        {
            DrawScrollingChannelsTab();
            DrawTrackerChannelsTab();
            DrawAurasTab();
            DrawFiltersTab();
            DrawSettingsTab();
        }
    }

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
                        Cfg.Channels.Add(new DisplayChannel { Name = $"Scrolling Text {Cfg.Channels.Count + 1}", Mode = ChannelMode.Scrolling });
                        Cfg.Save();
                    }
                    ImGui.Separator();

                    for (int i = 0; i < Cfg.Channels.Count; i++)
                    {
                        var ch = Cfg.Channels[i];
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
                    if (selectedScrollChannelIndex >= 0 && selectedScrollChannelIndex < Cfg.Channels.Count && Cfg.Channels[selectedScrollChannelIndex].Mode == ChannelMode.Scrolling)
                    {
                        DrawChannelSettings(Cfg.Channels[selectedScrollChannelIndex], selectedScrollChannelIndex);
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
                        Cfg.Channels.Add(new DisplayChannel { Name = $"New Tracker {Cfg.Channels.Count + 1}", Mode = ChannelMode.Tracker, Direction = ScrollDirection.Right, TrackerStyle = TrackerStyle.IconDial });
                        Cfg.Save();
                    }
                    if (ImGui.Button("+ Add Overlay", new Vector2(-1, 30)))
                    {
                        Cfg.Channels.Add(new DisplayChannel { Name = $"New Overlay {Cfg.Channels.Count + 1}", Mode = ChannelMode.Overlay, Direction = ScrollDirection.Static, NormalScale = 2.0f });
                        Cfg.Save();
                    }
                    ImGui.Separator();

                    for (int i = 0; i < Cfg.Channels.Count; i++)
                    {
                        var ch = Cfg.Channels[i];
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
                    if (selectedTrackerChannelIndex >= 0 && selectedTrackerChannelIndex < Cfg.Channels.Count && Cfg.Channels[selectedTrackerChannelIndex].Mode != ChannelMode.Scrolling)
                    {
                        DrawChannelSettings(Cfg.Channels[selectedTrackerChannelIndex], selectedTrackerChannelIndex, Cfg.Channels[selectedTrackerChannelIndex].Mode);
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

        ImGui.PushItemWidth(250f);
        string chName = ch.Name;
        if (ImGui.InputText("Channel Name", ref chName, 50)) { ch.Name = chName; changed = true; }
        ImGui.PopItemWidth();

        ImGui.SameLine(ImGui.GetWindowWidth() - 100);
        if (ImGui.Button("Delete", new Vector2(80, 25))) { Cfg.Channels.RemoveAt(index); Cfg.Save(); return; }

        bool enabled = ch.Enabled;
        if (ImGui.Checkbox("Enable Channel", ref enabled)) { ch.Enabled = enabled; changed = true; }

        ImGui.SameLine(); ImGui.Dummy(new Vector2(20, 0)); ImGui.SameLine();
        if (ImGui.Button("Test", new Vector2(80, 25))) Plugin.SpawnTestText(false, ch);
        if (mode == ChannelMode.Scrolling) { ImGui.SameLine(); if (ImGui.Button("Test (Crit)", new Vector2(100, 25))) Plugin.SpawnTestText(true, ch); }

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
                    ImGui.PushItemWidth(160f);

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
                            string[] dirNamesTracker = { "Grow Up", "Grow Down", "Left (Horizontal)", "Right (Horizontal)" };
                            int tDir = ch.Direction == ScrollDirection.Up ? 0 : (ch.Direction == ScrollDirection.Down ? 1 : (ch.Direction == ScrollDirection.Left ? 2 : 3));
                            if (ImGui.Combo("List Direction", ref tDir, dirNamesTracker, dirNamesTracker.Length))
                            {
                                ch.Direction = tDir == 0 ? ScrollDirection.Up : (tDir == 1 ? ScrollDirection.Down : (tDir == 2 ? ScrollDirection.Left : ScrollDirection.Right));
                                changed = true;
                            }
                        }
                        ImGui.Unindent(10f);
                    }
                    ImGui.PopItemWidth();
                }
            }

            if (mode == ChannelMode.Scrolling)
            {
                using (var tabAnim = ImRaii.TabItem("Movement"))
                {
                    if (tabAnim)
                    {
                        ImGui.Spacing();
                        ImGui.PushItemWidth(160f);
                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "Animation Rules");
                        ImGui.Indent(10f);
                        string[] dirNames = { "Scroll Up", "Scroll Down", "Scroll Left", "Scroll Right", "Static", "Pop", "Fade" };
                        int cDir = (int)ch.Direction; if (ImGui.Combo("Spawn Animation", ref cDir, dirNames, dirNames.Length)) { ch.Direction = (ScrollDirection)cDir; changed = true; }
                        float cCurve = ch.Curve; if (ImGui.DragFloat("Arc Curvature", ref cCurve, 0.5f, -100f, 100f)) { ch.Curve = cCurve; changed = true; }
                        float cSpeed = ch.Speed; if (ImGui.DragFloat("Scroll Speed", ref cSpeed, 1f, 10f, 300f)) { ch.Speed = cSpeed; changed = true; }
                        float cDur = ch.Duration; if (ImGui.DragFloat("Normal Duration (sec)", ref cDur, 0.05f, 0.5f, 10.0f)) { ch.Duration = cDur; changed = true; }
                        float cFade = ch.FadeDuration; if (ImGui.DragFloat("Fade Duration (sec)", ref cFade, 0.05f, 0.0f, 5.0f)) { ch.FadeDuration = cFade; changed = true; }
                        ImGui.Unindent(10f);
                        ImGui.PopItemWidth();
                    }
                }

                using (var tabCrits = ImRaii.TabItem("Crits & Big Hits"))
                {
                    if (tabCrits)
                    {
                        ImGui.Spacing();
                        ImGui.PushItemWidth(160f);
                        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Critical Hit Dynamics");
                        ImGui.Indent(10f);

                        string[] soundOptions = new string[17]; soundOptions[0] = "No Sound"; for (int k = 1; k <= 16; k++) soundOptions[k] = $"Sound Effect {k} (<se.{k}>)";
                        int csound = ch.CritSound; if (ImGui.Combo("Crit Sound", ref csound, soundOptions, soundOptions.Length)) { ch.CritSound = csound; changed = true; }

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
                        ImGui.PopItemWidth();
                    }
                }
            }

            using (var tabVis = ImRaii.TabItem("Text & Visuals"))
            {
                if (tabVis)
                {
                    ImGui.Spacing();
                    string fontName = ch.FontFileName ?? "";
                    ImGui.PushItemWidth(250f);
                    if (ImGui.InputText("Custom Font (.ttf)", ref fontName, 256)) { ch.FontFileName = fontName; changed = true; }
                    ImGui.PopItemWidth();
                    DrawHelpMarker("Leave blank to use Global Font.");

                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                    if (mode == ChannelMode.Scrolling)
                    {
                        ImGui.PushItemWidth(150f);
                        int spam = ch.SpamThreshold; if (ImGui.DragInt("Hide Damage Below", ref spam, 50, 0, 100000)) { ch.SpamThreshold = spam; changed = true; }
                        ImGui.PopItemWidth();
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
                        ImGui.SameLine(); ImGui.PushItemWidth(100f);
                        int maxL = ch.MaxSkillNameLength; if (ImGui.DragInt("Max Chars", ref maxL, 1f, 5, 40)) { ch.MaxSkillNameLength = maxL; changed = true; }
                        ImGui.PopItemWidth();
                    }

                    bool sAbs = ch.ShowAbsorbs; if (ImGui.Checkbox("Show Full Absorbs (Damage = 0)", ref sAbs)) { ch.ShowAbsorbs = sAbs; changed = true; }
                    if (sAbs)
                    {
                        ImGui.SameLine(); ImGui.PushItemWidth(150f);
                        string absT = ch.AbsorbText; if (ImGui.InputText("Absorb Text", ref absT, 30)) { ch.AbsorbText = absT; changed = true; }
                        ImGui.PopItemWidth();
                    }

                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                    bool sPulse = ch.PulseEffect; if (ImGui.Checkbox("Text Pulse (Scale Animation)", ref sPulse)) { ch.PulseEffect = sPulse; changed = true; }
                    if (sPulse)
                    {
                        ImGui.Indent(10f); ImGui.PushItemWidth(150f);
                        float pSpd = ch.PulseSpeed; if (ImGui.DragFloat("Pulse Speed", ref pSpd, 0.1f, 0.5f, 20.0f)) { ch.PulseSpeed = pSpd; changed = true; }
                        float pAmp = ch.PulseAmplitude; if (ImGui.DragFloat("Pulse Amplitude", ref pAmp, 0.01f, 0.01f, 1.0f)) { ch.PulseAmplitude = pAmp; changed = true; }
                        ImGui.PopItemWidth(); ImGui.Unindent(10f);
                    }

                    if (mode == ChannelMode.Scrolling)
                    {
                        ImGui.Spacing();
                        ImGui.PushItemWidth(250f);
                        string[] soundOptions2 = new string[17]; soundOptions2[0] = "No Sound"; for (int k = 1; k <= 16; k++) soundOptions2[k] = $"Sound Effect {k} (<se.{k}>)";
                        int asound = ch.AlertSound; if (ImGui.Combo("Sound on Appear (Alerts)", ref asound, soundOptions2, soundOptions2.Length)) { ch.AlertSound = asound; changed = true; }
                        ImGui.PopItemWidth();
                    }
                }
            }
        }
        if (changed) Cfg.Save();
    }

    private void DrawAurasTab()
    {
        using var tab = ImRaii.TabItem("Auras & Triggers");
        if (tab)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1f), "System Events (Sent to channels with Alerts enabled)");
            ImGui.Separator();
            ImGui.Spacing();

            bool changedSys = false;
            bool lowHp = Cfg.TriggerLowHp; if (ImGui.Checkbox("Low Health Warning", ref lowHp)) { Cfg.TriggerLowHp = lowHp; changedSys = true; }
            if (lowHp)
            {
                ImGui.Indent(10f); ImGui.PushItemWidth(150f);
                int hpThresh = Cfg.LowHpThresholdPercent; if (ImGui.DragInt("Health Threshold (%)", ref hpThresh, 1f, 5, 50)) { Cfg.LowHpThresholdPercent = hpThresh; changedSys = true; }
                string txtHp = Cfg.TriggerTextLowHp; if (ImGui.InputText("Text##hp", ref txtHp, 50)) { Cfg.TriggerTextLowHp = txtHp; changedSys = true; }
                ImGui.PopItemWidth(); ImGui.Unindent(10f);
            }

            bool lowMp = Cfg.TriggerLowMp; if (ImGui.Checkbox("Low Mana Warning", ref lowMp)) { Cfg.TriggerLowMp = lowMp; changedSys = true; }
            if (lowMp)
            {
                ImGui.Indent(10f); ImGui.PushItemWidth(150f);
                int mpThresh = Cfg.LowMpThresholdValue; if (ImGui.DragInt("Mana Threshold (Units)", ref mpThresh, 50f, 500, 5000)) { Cfg.LowMpThresholdValue = mpThresh; changedSys = true; }
                string txtMp = Cfg.TriggerTextLowMp; if (ImGui.InputText("Text##mp", ref txtMp, 50)) { Cfg.TriggerTextLowMp = txtMp; changedSys = true; }
                ImGui.PopItemWidth(); ImGui.Unindent(10f);
            }

            bool ccTrigger = Cfg.TriggerLossOfControl; if (ImGui.Checkbox("Loss of Control Warning", ref ccTrigger)) { Cfg.TriggerLossOfControl = ccTrigger; changedSys = true; }
            if (ccTrigger)
            {
                ImGui.Indent(10f); ImGui.PushItemWidth(150f);
                string txtCc = Cfg.TriggerTextLossOfControl; if (ImGui.InputText("Text##cc", ref txtCc, 50)) { Cfg.TriggerTextLossOfControl = txtCc; changedSys = true; }
                ImGui.PopItemWidth(); ImGui.Unindent(10f);
            }
            if (changedSys) Cfg.Save();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.1f, 1f), "Unified Aura System (WeakAuras)");
            ImGui.Text("Add a Buff ID, select target channels, and configure complex conditions.");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushItemWidth(200f);
            ImGui.InputText("Search Buff/Debuff", ref searchInputTriggers, 100);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Search Status##Triggers")) PerformSearch(searchInputTriggers, true);

            if (searchResultsTriggers.Count > 0)
            {
                ImGui.Spacing();
                using var resultsChild = ImRaii.Child("SearchResultsRegionTriggers", new Vector2(0, 150), true);
                if (resultsChild)
                {
                    foreach (var res in searchResultsTriggers)
                    {
                        ImGui.Text($"{res.Name} [ID: {res.ID}]");
                        ImGui.SameLine(300f);
                        if (ImGui.Button($"Add##addTrg_{res.ID}"))
                        {
                            var newTrg = new AuraTrigger { StatusId = res.ID };
                            var firstCh = Cfg.Channels.FirstOrDefault()?.Name;
                            if (firstCh != null) newTrg.TargetChannels.Add(firstCh);

                            Cfg.AuraTriggers.Insert(0, newTrg);
                            Cfg.Save();
                        }
                    }
                }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.4f, 1f, 0.8f, 1f), "Import Aura");
            ImGui.PushItemWidth(300f);
            ImGui.InputText("Aura Code", ref auraImportBuffer, 5000);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Add from Code##importAura"))
            {
                try
                {
                    string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auraImportBuffer));
                    var imported = Newtonsoft.Json.JsonConvert.DeserializeObject<AuraTrigger>(json);
                    if (imported != null) { Cfg.AuraTriggers.Insert(0, imported); Cfg.Save(); auraImportBuffer = ""; }
                }
                catch { }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            string[] channelNames = Cfg.Channels.Select(c => c.Name).ToArray();
            string[] soundOptions = new string[17]; soundOptions[0] = "Default (From channel settings)";
            for (int k = 1; k <= 16; k++) soundOptions[k] = $"Sound Effect {k} (<se.{k}>)";

            using var aurasRegion = ImRaii.Child("AurasRegion", new Vector2(0, 400), true);
            if (aurasRegion)
            {
                for (int i = 0; i < Cfg.AuraTriggers.Count; i++)
                {
                    var trg = Cfg.AuraTriggers[i];
                    bool changedTrg = false;

                    using var blockId = ImRaii.PushId($"trg_block_{i}");
                    using var group = ImRaii.Group();

                    bool en = trg.Enabled;
                    if (ImGui.Checkbox("##en", ref en)) { trg.Enabled = en; changedTrg = true; }
                    ImGui.SameLine();

                    string name = Plugin.Parser?.GetSkillName(trg.StatusId) ?? "Unknown";
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"{name} (ID: {trg.StatusId})");

                    ImGui.SameLine(ImGui.GetWindowWidth() - 170);
                    if (ImGui.Button("Export", new Vector2(70, 24)))
                    {
                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(trg, Newtonsoft.Json.Formatting.None);
                        ImGui.SetClipboardText(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)));
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete", new Vector2(70, 24)))
                    {
                        Cfg.AuraTriggers.RemoveAt(i);
                        Cfg.Save();
                        break;
                    }

                    ImGui.Indent(30f);

                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Output to (multiple allowed):");
                    ImGui.Indent(10f);

                    if (!string.IsNullOrEmpty(trg.TargetChannelName))
                    {
                        if (!trg.TargetChannels.Contains(trg.TargetChannelName))
                            trg.TargetChannels.Add(trg.TargetChannelName);
                        trg.TargetChannelName = "";
                        changedTrg = true;
                    }

                    int col = 0;
                    foreach (var chName in channelNames)
                    {
                        bool isChecked = trg.TargetChannels.Contains(chName);
                        if (ImGui.Checkbox($"{chName}##ch_{i}_{chName}", ref isChecked))
                        {
                            if (isChecked) trg.TargetChannels.Add(chName);
                            else trg.TargetChannels.Remove(chName);
                            changedTrg = true;
                        }

                        col++;
                        if (col < 2) ImGui.SameLine(250f);
                        else { col = 0; ImGui.NewLine(); }
                    }
                    if (col != 0) ImGui.NewLine();
                    ImGui.Unindent(10f);

                    bool ocbm = trg.OnlyCastByMe;
                    if (ImGui.Checkbox("Track only MY applications", ref ocbm)) { trg.OnlyCastByMe = ocbm; changedTrg = true; }

                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Custom Text:"); ImGui.SameLine(200f);
                    ImGui.PushItemWidth(250f);
                    string msg = trg.CustomText;
                    if (ImGui.InputText("##txt", ref msg, 50)) { trg.CustomText = msg; changedTrg = true; }
                    ImGui.PopItemWidth();
                    DrawHelpMarker("Leave blank to display the original skill name.");

                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Sound on appear:"); ImGui.SameLine(200f);
                    ImGui.PushItemWidth(250f);
                    int snd = trg.SoundOverride;
                    if (ImGui.Combo("##snd", ref snd, soundOptions, soundOptions.Length)) { trg.SoundOverride = snd; changedTrg = true; }
                    ImGui.PopItemWidth();

                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.4f, 1f, 0.8f, 1f), "Complex Conditions (AND)");
                    if (ImGui.Button("+ Add Condition", new Vector2(150, 24))) { trg.Conditions.Add(new TriggerCondition()); changedTrg = true; }

                    if (trg.Conditions.Count > 0)
                    {
                        ImGui.Indent(10f);
                        for (int c = 0; c < trg.Conditions.Count; c++)
                        {
                            var cond = trg.Conditions[c];
                            using var condId = ImRaii.PushId($"cond_{c}");

                            ImGui.PushItemWidth(200f);
                            int cType = (int)cond.Type;
                            if (ImGui.Combo("##cType", ref cType, conditionTypes, conditionTypes.Length)) { cond.Type = (ConditionType)cType; changedTrg = true; }
                            ImGui.PopItemWidth();

                            if (cond.Type != ConditionType.None)
                            {
                                if (cond.Type == ConditionType.PlayerHP || cond.Type == ConditionType.TargetHP)
                                {
                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(100f);
                                    int cOp = (int)cond.Operator;
                                    if (ImGui.Combo("##cOp", ref cOp, conditionOperators, conditionOperators.Length)) { cond.Operator = (ConditionOperator)cOp; changedTrg = true; }
                                    ImGui.PopItemWidth();

                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(100f);
                                    float cVal = cond.Value;
                                    if (ImGui.DragFloat("##cVal", ref cVal, 1f, 0f, 100f, "%.1f%%")) { cond.Value = cVal; changedTrg = true; }
                                    ImGui.PopItemWidth();
                                }
                                else if (cond.Type == ConditionType.PlayerAuraStacks || cond.Type == ConditionType.TargetAuraStacks)
                                {
                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(80f);
                                    int targetId = (int)cond.TargetStatusId;
                                    if (ImGui.InputInt("Buff ID##cTargetId", ref targetId, 0, 0)) { cond.TargetStatusId = (uint)Math.Max(0, targetId); changedTrg = true; }
                                    ImGui.PopItemWidth();

                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(80f);
                                    int cOp = (int)cond.Operator;
                                    if (ImGui.Combo("##cOp", ref cOp, conditionOperators, conditionOperators.Length)) { cond.Operator = (ConditionOperator)cOp; changedTrg = true; }
                                    ImGui.PopItemWidth();

                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(80f);
                                    int cVal = (int)cond.Value;
                                    if (ImGui.InputInt("Stacks##cVal", ref cVal, 0, 0)) { cond.Value = cVal; changedTrg = true; }
                                    ImGui.PopItemWidth();
                                }
                                else
                                {
                                    ImGui.SameLine();
                                    ImGui.PushItemWidth(150f);
                                    int cValInt = (int)cond.Value;
                                    if (ImGui.InputInt("ID##cValID", ref cValInt, 0, 0)) { cond.Value = cValInt; changedTrg = true; }
                                    ImGui.PopItemWidth();
                                }
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("X##delCond")) { trg.Conditions.RemoveAt(c); changedTrg = true; break; }
                        }
                        ImGui.Unindent(10f);
                    }

                    ImGui.Unindent(30f);
                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                    if (changedTrg) Cfg.Save();
                }
            }
        }
    }

    private void PerformSearch(string query, bool isForTriggers = false)
    {
        var targetList = isForTriggers ? searchResultsTriggers : searchResultsBlacklist;
        targetList.Clear();
        if (string.IsNullOrWhiteSpace(query)) return;

        string q = query.ToLowerInvariant();
        int count = 0;

        bool isNumeric = uint.TryParse(q, out uint searchId);
        if (!isNumeric && q.Length < 2) return;

        try
        {
            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (actionSheet != null && !isForTriggers)
            {
                foreach (var act in actionSheet)
                {
                    if (isNumeric && act.RowId == searchId) { targetList.Add((act.RowId, act.Name.ToString(), false)); count++; break; }
                    else if (!isNumeric) { string name = act.Name.ToString(); if (!string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains(q)) { targetList.Add((act.RowId, name, false)); count++; if (count > 40) break; } }
                }
            }
        }
        catch { }

        count = 0;
        try
        {
            var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (statusSheet != null)
            {
                foreach (var stat in statusSheet)
                {
                    if (isNumeric && stat.RowId == searchId) { targetList.Add((stat.RowId, stat.Name.ToString(), true)); count++; break; }
                    else if (!isNumeric) { string name = stat.Name.ToString(); if (!string.IsNullOrEmpty(name) && name.ToLowerInvariant().Contains(q)) { targetList.Add((stat.RowId, name, true)); count++; if (count > 40) break; } }
                }
            }
        }
        catch { }
    }

    private string GetSkillName(uint id)
    {
        try
        {
            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (actionSheet != null) { var actionRow = actionSheet.GetRow(id); string name = actionRow.Name.ToString(); if (!string.IsNullOrEmpty(name)) return name; }
            var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (statusSheet != null) { var statusRow = statusSheet.GetRow(id); string name = statusRow.Name.ToString(); if (!string.IsNullOrEmpty(name)) return name; }
        }
        catch { }
        return "Unknown Skill";
    }

    private void DrawFiltersTab()
    {
        using var tab = ImRaii.TabItem("Blacklist");
        if (tab)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Skill Blacklist (Ignore everywhere)");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.PushItemWidth(200f);
            ImGui.InputText("Name or ID", ref searchInputBlacklist, 100);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Search in Game Database##Blacklist")) PerformSearch(searchInputBlacklist, false);

            if (searchResultsBlacklist.Count > 0)
            {
                ImGui.Spacing();
                using var sList = ImRaii.Child("SearchResultsRegionBlacklist", new Vector2(0, 150), true);
                if (sList)
                {
                    foreach (var res in searchResultsBlacklist)
                    {
                        bool isBanned = Cfg.BlacklistedSkillIds.Contains(res.ID);
                        if (ImGui.Checkbox($"{res.Name} [ID: {res.ID}]##searchBlk{res.ID}", ref isBanned))
                        {
                            if (isBanned) Cfg.BlacklistedSkillIds.Add(res.ID); else Cfg.BlacklistedSkillIds.Remove(res.ID);
                            Cfg.Save();
                        }
                    }
                }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Currently hidden (uncheck to restore):");

            using var blRegion = ImRaii.Child("BlacklistRegion", new Vector2(0, 200), true);
            if (blRegion)
            {
                for (int i = Cfg.BlacklistedSkillIds.Count - 1; i >= 0; i--)
                {
                    uint id = Cfg.BlacklistedSkillIds[i]; string name = GetSkillName(id); bool isChecked = true;
                    if (ImGui.Checkbox($"{name} (ID: {id})##banned{id}", ref isChecked)) { if (!isChecked) { Cfg.BlacklistedSkillIds.RemoveAt(i); Cfg.Save(); } }
                }
            }
        }
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings & Profiles");
        if (tab)
        {
            using var subTabs = ImRaii.TabBar("SettingsSubTabs");
            if (subTabs)
            {
                using (var tabColors = ImRaii.TabItem("Colors"))
                {
                    if (tabColors)
                    {
                        ImGui.Spacing();
                        Vector4 cZ1 = Cfg.ColorZone1; Vector4 cZ1C = Cfg.ColorZone1Crit; Vector4 cZ2 = Cfg.ColorZone2;
                        Vector4 cPhys = Cfg.ColorPhysical; Vector4 cMag = Cfg.ColorMagical; Vector4 cUniq = Cfg.ColorUnique;
                        Vector4 cBigHit = Cfg.ColorBigHit;
                        Vector4 cHeal = Cfg.ColorHeal; Vector4 cMp = Cfg.ColorMp; Vector4 cStat = Cfg.ColorStatus;
                        Vector4 cStatFading = Cfg.ColorStatusFading; Vector4 cZ4 = Cfg.ColorZone4; Vector4 cOut = Cfg.ColorOutline;

                        bool changed = false;
                        var colorFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar;

                        if (ImGui.ColorEdit4("My Damage", ref cZ1, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("My Crits", ref cZ1C, colorFlags)) changed = true;
                        ImGui.Spacing();
                        if (ImGui.ColorEdit4("Incoming Damage", ref cZ2, colorFlags)) changed = true;
                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                        ImGui.TextColored(new Vector4(0.4f, 1f, 0.8f, 1f), "Damage Type & Special Colors");
                        if (ImGui.ColorEdit4("Physical Damage", ref cPhys, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("Magical Damage", ref cMag, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("Unique/Pure Damage", ref cUniq, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("Big Hit Color", ref cBigHit, colorFlags)) changed = true;

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        if (ImGui.ColorEdit4("Healing (HP)", ref cHeal, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("Recovery (MP)", ref cMp, colorFlags)) changed = true;
                        ImGui.Spacing();
                        if (ImGui.ColorEdit4("Statuses (Gain)", ref cStat, colorFlags)) changed = true;
                        if (ImGui.ColorEdit4("Statuses (Fade)", ref cStatFading, colorFlags)) changed = true;
                        ImGui.Spacing();
                        if (ImGui.ColorEdit4("Alerts / Triggers", ref cZ4, colorFlags)) changed = true;

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        if (ImGui.ColorEdit4("Outline Color", ref cOut, colorFlags)) changed = true;

                        if (changed)
                        {
                            Cfg.ColorZone1 = cZ1; Cfg.ColorZone1Crit = cZ1C; Cfg.ColorZone2 = cZ2;
                            Cfg.ColorPhysical = cPhys; Cfg.ColorMagical = cMag; Cfg.ColorUnique = cUniq; Cfg.ColorBigHit = cBigHit;
                            Cfg.ColorHeal = cHeal; Cfg.ColorMp = cMp; Cfg.ColorStatus = cStat;
                            Cfg.ColorStatusFading = cStatFading; Cfg.ColorZone4 = cZ4; Cfg.ColorOutline = cOut;
                            Cfg.Save();
                        }
                    }
                }

                using (var tabGlobals = ImRaii.TabItem("Global Effects"))
                {
                    if (tabGlobals)
                    {
                        ImGui.Spacing();
                        bool throttle = Cfg.EnableThrottling;
                        if (ImGui.Checkbox("Merge frequent hits (show x2, x3)", ref throttle)) { Cfg.EnableThrottling = throttle; Cfg.Save(); }
                        if (throttle) { float window = Cfg.ThrottleTimeWindow; ImGui.Indent(10f); ImGui.PushItemWidth(150f); if (ImGui.DragFloat("Merge Window (sec)", ref window, 0.05f, 0.1f, 2.0f)) { Cfg.ThrottleTimeWindow = window; Cfg.Save(); } ImGui.PopItemWidth(); ImGui.Unindent(10f); }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        bool debugIds = Cfg.DebugShowIds; if (ImGui.Checkbox("Show Skill IDs instead of Icons", ref debugIds)) { Cfg.DebugShowIds = debugIds; Cfg.Save(); }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        string fontName = Cfg.FontFileName ?? ""; ImGui.PushItemWidth(250f); if (ImGui.InputText("Global Font (.ttf)", ref fontName, 256)) { Cfg.FontFileName = fontName; Cfg.Save(); }
                        ImGui.PopItemWidth();
                        DrawHelpMarker("Leave blank to use the Global Font. File must be in the plugin folder.");
                        if (ImGui.Button("Open Plugin Folder", new Vector2(250, 25))) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = Plugin.PluginInterface.ConfigDirectory.FullName, UseShellExecute = true }); }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        bool outline = Cfg.EnableOutline; int format = (int)Cfg.FormatType;
                        ImGui.PushItemWidth(160f); string[] formats = { "10000 (Merged)", "10 000 (Space)", "10,000 (Comma)", "10k / 1.5M (Smart)" }; bool chFmt = ImGui.Combo("Number Format", ref format, formats, formats.Length); ImGui.PopItemWidth();
                        bool chOut = ImGui.Checkbox("Enable Text Outline", ref outline);
                        if (chOut || chFmt) { Cfg.EnableOutline = outline; Cfg.FormatType = (NumberFormatType)format; Cfg.Save(); }
                    }
                }

                using (var tabProfiles = ImRaii.TabItem("Profiles"))
                {
                    if (tabProfiles)
                    {
                        ImGui.Spacing();
                        ImGui.PushItemWidth(250f); ImGui.InputText("New Preset Name", ref newPresetName, 50); ImGui.PopItemWidth();
                        ImGui.SameLine(); if (ImGui.Button("Save Current Settings")) { if (!string.IsNullOrWhiteSpace(newPresetName)) { Cfg.SavedPresets[newPresetName] = Cfg.ExportToBase64(); Cfg.Save(); newPresetName = ""; } }

                        ImGui.Spacing();
                        if (Cfg.SavedPresets.Count > 0)
                        {
                            using var presetList = ImRaii.Child("PresetsList", new Vector2(0, 120), true);
                            if (presetList)
                            {
                                foreach (var preset in Cfg.SavedPresets.Keys.ToList())
                                {
                                    ImGui.Text(preset); ImGui.SameLine(ImGui.GetWindowWidth() - 220);
                                    if (ImGui.Button($"Load##{preset}")) Cfg.ImportFromBase64(Cfg.SavedPresets[preset]); ImGui.SameLine();
                                    if (ImGui.Button($"Update##{preset}")) { Cfg.SavedPresets[preset] = Cfg.ExportToBase64(); Cfg.Save(); }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"X##{preset}")) { Cfg.SavedPresets.Remove(preset); foreach (var kvp in Cfg.ClassPresets.ToList()) if (kvp.Value == preset) Cfg.ClassPresets.Remove(kvp.Key); Cfg.Save(); }
                                }
                            }
                        }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        bool autoSwitch = Cfg.AutoSwitchPresets; if (ImGui.Checkbox("Automatically apply preset on job change", ref autoSwitch)) { Cfg.AutoSwitchPresets = autoSwitch; Cfg.Save(); }
                        if (autoSwitch)
                        {
                            ImGui.Spacing(); var presetNames = Cfg.SavedPresets.Keys.ToList(); presetNames.Insert(0, "--- None Selected ---"); string[] presetArray = presetNames.ToArray();
                            using var classBinds = ImRaii.Child("ClassBindings", new Vector2(0, 250), true);
                            if (classBinds)
                            {
                                foreach (var group in jobGroups)
                                {
                                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), group.Group); ImGui.Indent(10f); int columns = 2; int currentColumn = 0;
                                    foreach (uint jobId in group.Jobs)
                                    {
                                        string abbr = GetJobAbbreviation(jobId); string currentPreset = Cfg.ClassPresets.ContainsKey(jobId) ? Cfg.ClassPresets[jobId] : "--- None Selected ---";
                                        int currentIndex = Array.IndexOf(presetArray, currentPreset); if (currentIndex == -1) currentIndex = 0;
                                        ImGui.PushItemWidth(120f);
                                        if (ImGui.Combo($"{abbr}##job{jobId}", ref currentIndex, presetArray, presetArray.Length))
                                        {
                                            if (currentIndex == 0) Cfg.ClassPresets.Remove(jobId); else Cfg.ClassPresets[jobId] = presetArray[currentIndex]; Cfg.Save();
                                        }
                                        ImGui.PopItemWidth(); currentColumn++; if (currentColumn < columns) ImGui.SameLine(currentColumn * 200f); else currentColumn = 0;
                                    }
                                    if (currentColumn != 0) ImGui.NewLine(); ImGui.Unindent(10f); ImGui.Spacing();
                                }
                            }
                        }

                        ImGui.Spacing(); ImGui.Spacing(); ImGui.Separator();
                        if (ImGui.Button("Copy Code to Clipboard", new Vector2(250, 25))) ImGui.SetClipboardText(Cfg.ExportToBase64());
                        ImGui.SameLine(); ImGui.PushItemWidth(150f); ImGui.InputText("##import", ref importBuffer, 10000); ImGui.PopItemWidth(); ImGui.SameLine();
                        if (ImGui.Button("Paste Code")) { if (!string.IsNullOrEmpty(importBuffer)) { Cfg.ImportFromBase64(importBuffer); importBuffer = ""; } }
                    }
                }
            }
        }
    }

    private string GetJobAbbreviation(uint jobId)
    {
        try
        {
            var jobSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
            if (jobSheet != null) { var jobRow = jobSheet.GetRow(jobId); string abbr = jobRow.Abbreviation.ToString(); if (!string.IsNullOrEmpty(abbr)) return abbr; }
        }
        catch { }
        return $"Job {jobId}";
    }
}
