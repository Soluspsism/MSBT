using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

internal sealed partial class ConfigWindow
{
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
                        Vector4 cZ1 = configuration.ColorZone1; Vector4 cZ1C = configuration.ColorZone1Crit; Vector4 cZ2 = configuration.ColorZone2;
                        Vector4 cPhys = configuration.ColorPhysical; Vector4 cMag = configuration.ColorMagical; Vector4 cUniq = configuration.ColorUnique;
                        Vector4 cBigHit = configuration.ColorBigHit;
                        Vector4 cHeal = configuration.ColorHeal; Vector4 cMp = configuration.ColorMp; Vector4 cStat = configuration.ColorStatus;
                        Vector4 cStatFading = configuration.ColorStatusFading; Vector4 cZ4 = configuration.ColorZone4; Vector4 cOut = configuration.ColorOutline;

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
                        if (ImGui.ColorEdit4("Text Effect Color", ref cOut, colorFlags)) changed = true;

                        if (changed)
                        {
                            configuration.ColorZone1 = cZ1; configuration.ColorZone1Crit = cZ1C; configuration.ColorZone2 = cZ2;
                            configuration.ColorPhysical = cPhys; configuration.ColorMagical = cMag; configuration.ColorUnique = cUniq; configuration.ColorBigHit = cBigHit;
                            configuration.ColorHeal = cHeal; configuration.ColorMp = cMp; configuration.ColorStatus = cStat;
                            configuration.ColorStatusFading = cStatFading; configuration.ColorZone4 = cZ4; configuration.ColorOutline = cOut;
                            configuration.Save();
                        }
                    }
                }

                using (var tabGlobals = ImRaii.TabItem("Global Effects"))
                {
                    if (tabGlobals)
                    {
                        ImGui.Spacing();
                        bool throttle = configuration.EnableThrottling;
                        if (ImGui.Checkbox("Merge frequent hits (show x2, x3)", ref throttle)) { configuration.EnableThrottling = throttle; configuration.Save(); }
                        if (throttle)
                        {
                            float window = configuration.ThrottleTimeWindow;
                            ImGui.Indent(10f);
                            using (ImRaii.ItemWidth(150f))
                            {
                                if (ImGui.DragFloat("Merge Window (sec)", ref window, 0.05f, 0.1f, 2.0f)) { configuration.ThrottleTimeWindow = window; configuration.Save(); }
                            }
                            ImGui.Unindent(10f);
                        }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        bool debugIds = configuration.DebugShowIds; if (ImGui.Checkbox("Show Skill IDs instead of Icons", ref debugIds)) { configuration.DebugShowIds = debugIds; configuration.Save(); }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        string fontName = configuration.FontKey ?? "";
                        using (ImRaii.ItemWidth(250f))
                        {
                            if (DrawFontSelector("Global Font", ref fontName, false)) { configuration.FontKey = fontName; configuration.Save(); }
                        }
                        DrawHelpMarker("FFXIV fonts are built in. Put .ttf or .otf files in the Fonts folder, then reload.");
                        float fontSize = configuration.FontSize;
                        using (ImRaii.ItemWidth(150f))
                        {
                            if (ImGui.DragFloat("Global Font Size", ref fontSize, 1f, 8f, 96f, "%.0f px"))
                            {
                                configuration.FontSize = fontSize;
                                configuration.Save();
                            }
                        }
                        if (ImGui.Button("Open Fonts Folder", new Vector2(160, 25)))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = plugin.FontManager.FontsDirectory, UseShellExecute = true });
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Reload Fonts", new Vector2(120, 25)))
                        {
                            plugin.FontManager.RefreshFonts();
                            configuration.Save();
                        }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        int effect = (int)configuration.TextEffect;
                        int format = (int)configuration.FormatType;
                        bool changedTextStyle;
                        using (ImRaii.ItemWidth(160f))
                        {
                            changedTextStyle = ImGui.Combo("Number Format", ref format, NumberFormats, NumberFormats.Length);
                            changedTextStyle |= ImGui.Combo("Text Effect", ref effect, TextEffects, TextEffects.Length);
                        }
                        float effectSize = configuration.TextEffectSize;
                        if (effect != (int)TextEffectType.None)
                        {
                            using (ImRaii.ItemWidth(160f))
                            {
                                string label = effect == (int)TextEffectType.Outline ? "Outline Thickness" : "Shadow Offset";
                                changedTextStyle |= ImGui.DragFloat(label, ref effectSize, 0.25f, 0.5f, 8f, "%.2f px");
                            }
                        }
                        if (changedTextStyle)
                        {
                            configuration.FormatType = (NumberFormatType)format;
                            configuration.TextEffect = (TextEffectType)effect;
                            configuration.TextEffectSize = effectSize;
                            configuration.Save();
                        }
                    }
                }

                using (var tabProfiles = ImRaii.TabItem("Profiles"))
                {
                    if (tabProfiles)
                    {
                        ImGui.Spacing();
                        using (ImRaii.ItemWidth(250f))
                        {
                            ImGui.InputText("New Preset Name", ref newPresetName, 50);
                        }
                        ImGui.SameLine(); if (ImGui.Button("Save Current Settings")) { if (!string.IsNullOrWhiteSpace(newPresetName)) { configuration.SavedPresets[newPresetName] = configuration.ExportToBase64(); configuration.Save(); newPresetName = ""; } }

                        ImGui.Spacing();
                        if (configuration.SavedPresets.Count > 0)
                        {
                            using var presetList = ImRaii.Child("PresetsList", new Vector2(0, 120), true);
                            if (presetList)
                            {
                                foreach (var preset in configuration.SavedPresets.Keys.ToList())
                                {
                                    ImGui.Text(preset); ImGui.SameLine(ImGui.GetWindowWidth() - 220);
                                    if (ImGui.Button($"Load##{preset}")) plugin.ImportConfiguration(configuration.SavedPresets[preset]); ImGui.SameLine();
                                    if (ImGui.Button($"Update##{preset}")) { configuration.SavedPresets[preset] = configuration.ExportToBase64(); configuration.Save(); }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"X##{preset}")) { configuration.SavedPresets.Remove(preset); foreach (var kvp in configuration.ClassPresets.ToList()) if (kvp.Value == preset) configuration.ClassPresets.Remove(kvp.Key); configuration.Save(); }
                                }
                            }
                        }

                        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
                        bool autoSwitch = configuration.AutoSwitchPresets; if (ImGui.Checkbox("Automatically apply preset on job change", ref autoSwitch)) { configuration.AutoSwitchPresets = autoSwitch; configuration.Save(); }
                        if (autoSwitch)
                        {
                            ImGui.Spacing(); var presetNames = configuration.SavedPresets.Keys.ToList(); presetNames.Insert(0, "--- None Selected ---"); string[] presetArray = presetNames.ToArray();
                            using var classBinds = ImRaii.Child("ClassBindings", new Vector2(0, 250), true);
                            if (classBinds)
                            {
                                foreach (var group in jobGroups)
                                {
                                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), group.Group); ImGui.Indent(10f); int columns = 2; int currentColumn = 0;
                                    foreach (uint jobId in group.Jobs)
                                    {
                                        string abbr = GetJobAbbreviation(jobId); string currentPreset = configuration.ClassPresets.ContainsKey(jobId) ? configuration.ClassPresets[jobId] : "--- None Selected ---";
                                        int currentIndex = Array.IndexOf(presetArray, currentPreset); if (currentIndex == -1) currentIndex = 0;
                                        using (ImRaii.ItemWidth(120f))
                                        {
                                            if (ImGui.Combo($"{abbr}##job{jobId}", ref currentIndex, presetArray, presetArray.Length))
                                            {
                                                if (currentIndex == 0) configuration.ClassPresets.Remove(jobId); else configuration.ClassPresets[jobId] = presetArray[currentIndex]; configuration.Save();
                                            }
                                        }
                                        currentColumn++; if (currentColumn < columns) ImGui.SameLine(currentColumn * 200f); else currentColumn = 0;
                                    }
                                    if (currentColumn != 0) ImGui.NewLine(); ImGui.Unindent(10f); ImGui.Spacing();
                                }
                            }
                        }

                        ImGui.Spacing(); ImGui.Spacing(); ImGui.Separator();
                        if (ImGui.Button("Copy Code to Clipboard", new Vector2(250, 25))) ImGui.SetClipboardText(configuration.ExportToBase64());
                        ImGui.SameLine();
                        using (ImRaii.ItemWidth(150f))
                        {
                            ImGui.InputText("##import", ref importBuffer, 10000);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Paste Code")) { if (!string.IsNullOrEmpty(importBuffer)) { plugin.ImportConfiguration(importBuffer); importBuffer = ""; } }
                    }
                }
            }
        }
    }

    private string GetJobAbbreviation(uint jobId)
    {
        try
        {
            var jobSheet = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
            if (jobSheet != null) { var jobRow = jobSheet.GetRow(jobId); string abbr = jobRow.Abbreviation.ToString(); if (!string.IsNullOrEmpty(abbr)) return abbr; }
        }
        catch { }
        return $"Job {jobId}";
    }
}
