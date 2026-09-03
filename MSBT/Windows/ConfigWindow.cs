using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

internal sealed partial class ConfigWindow : Window
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;
    private readonly string[] critBehaviors = { "In main stream (Fly with others)", "Static burst on side (Only bumped)", "Convergence (Freeze on side, merge into stream)" };
    private readonly string[] alignNames = { "Left", "Center", "Right" };
    private readonly string[] trackerStyleNames = { "Text + Timer", "Icon + Digits Below", "Icon + Radial Dial", "Progress Bar" };

    private readonly string[] conditionTypes = { "No Condition", "My Health (%)", "Target Health (%)", "I HAVE buff (ID)", "I MISS buff (ID)", "Target HAS debuff (ID)", "Target MISSES debuff (ID)", "My buff stacks", "Target buff stacks" };
    private readonly string[] conditionOperators = { "Less Than (<)", "Greater Than (>)", "Equal To (==)" };
    private static readonly string[] TextEffects = { "None", "Shadow", "Outline" };
    private static readonly string[] NumberFormats = { "10000 (Merged)", "10 000 (Space)", "10,000 (Comma)", "10k / 1.5M (Smart)" };
    private static readonly string[] TrackerDirections = { "Grow Up", "Grow Down", "Left (Horizontal)", "Right (Horizontal)" };
    private static readonly string[] ScrollDirections = { "Scroll Up", "Scroll Down", "Scroll Left", "Scroll Right", "Static", "Pop", "Fade" };
    private static readonly string[] SoundOptions = CreateSoundOptions("No Sound");
    private static readonly string[] TriggerSoundOptions = CreateSoundOptions("Default (From channel settings)");

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
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(800, 850);
        configuration = plugin.Configuration;
        this.plugin = plugin;
    }

    public override void OnClose()
    {
        plugin.IsEditMode = false;
        ConfigRepository.SaveImmediate(configuration);
    }

    private void DrawHelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(?)");
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 35.0f))
            {
                ImGui.TextUnformatted(text);
            }
        }
    }

    private bool DrawFontSelector(string label, ref string fontReference, bool allowGlobal)
    {
        string normalized = plugin.FontManager.NormalizeReference(fontReference, allowGlobal);
        string[] options = allowGlobal ? plugin.FontManager.ChannelFontOptions : plugin.FontManager.FontOptions;
        int selected = allowGlobal && string.IsNullOrEmpty(normalized)
            ? 0
            : Array.IndexOf(options, normalized);
        if (selected < 0)
            selected = 0;

        if (!ImGui.Combo(label, ref selected, options, options.Length))
            return false;

        fontReference = allowGlobal && selected == 0 ? string.Empty : options[selected];
        return true;
    }

    private static string[] CreateSoundOptions(string firstOption)
    {
        var options = new string[17];
        options[0] = firstOption;
        for (int i = 1; i < options.Length; i++)
            options[i] = $"Sound Effect {i} (<se.{i}>)";
        return options;
    }

    public override void Draw()
    {
        bool editMode = plugin.IsEditMode;
        if (ImGui.Checkbox("Enable Edit Mode (Grid & Anchors)", ref editMode))
            plugin.IsEditMode = editMode;

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
}
