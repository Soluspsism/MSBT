using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

internal sealed partial class ConfigWindow
{
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
            var actionSheet = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
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
            var statusSheet = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
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
            var actionSheet = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (actionSheet != null) { var actionRow = actionSheet.GetRow(id); string name = actionRow.Name.ToString(); if (!string.IsNullOrEmpty(name)) return name; }
            var statusSheet = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
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

            using (ImRaii.ItemWidth(200f))
            {
                ImGui.InputText("Name or ID", ref searchInputBlacklist, 100);
            }
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
                        bool isBanned = configuration.BlacklistedSkillIds.Contains(res.ID);
                        if (ImGui.Checkbox($"{res.Name} [ID: {res.ID}]##searchBlk{res.ID}", ref isBanned))
                        {
                            if (isBanned) configuration.BlacklistedSkillIds.Add(res.ID); else configuration.BlacklistedSkillIds.Remove(res.ID);
                            configuration.Save();
                        }
                    }
                }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Currently hidden (uncheck to restore):");

            using var blRegion = ImRaii.Child("BlacklistRegion", new Vector2(0, 200), true);
            if (blRegion)
            {
                for (int i = configuration.BlacklistedSkillIds.Count - 1; i >= 0; i--)
                {
                    uint id = configuration.BlacklistedSkillIds[i]; string name = GetSkillName(id); bool isChecked = true;
                    if (ImGui.Checkbox($"{name} (ID: {id})##banned{id}", ref isChecked)) { if (!isChecked) { configuration.BlacklistedSkillIds.RemoveAt(i); configuration.Save(); } }
                }
            }
        }
    }
}

