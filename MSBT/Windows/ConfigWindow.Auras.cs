using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace MSBT.Windows;

internal sealed partial class ConfigWindow
{
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
            bool lowHp = configuration.TriggerLowHp; if (ImGui.Checkbox("Low Health Warning", ref lowHp)) { configuration.TriggerLowHp = lowHp; changedSys = true; }
            if (lowHp)
            {
                ImGui.Indent(10f);
                using (ImRaii.ItemWidth(150f))
                {
                    int hpThresh = configuration.LowHpThresholdPercent; if (ImGui.DragInt("Health Threshold (%)", ref hpThresh, 1f, 5, 50)) { configuration.LowHpThresholdPercent = hpThresh; changedSys = true; }
                    string txtHp = configuration.TriggerTextLowHp; if (ImGui.InputText("Text##hp", ref txtHp, 50)) { configuration.TriggerTextLowHp = txtHp; changedSys = true; }
                }
                ImGui.Unindent(10f);
            }

            bool lowMp = configuration.TriggerLowMp; if (ImGui.Checkbox("Low Mana Warning", ref lowMp)) { configuration.TriggerLowMp = lowMp; changedSys = true; }
            if (lowMp)
            {
                ImGui.Indent(10f);
                using (ImRaii.ItemWidth(150f))
                {
                    int mpThresh = configuration.LowMpThresholdValue; if (ImGui.DragInt("Mana Threshold (Units)", ref mpThresh, 50f, 500, 5000)) { configuration.LowMpThresholdValue = mpThresh; changedSys = true; }
                    string txtMp = configuration.TriggerTextLowMp; if (ImGui.InputText("Text##mp", ref txtMp, 50)) { configuration.TriggerTextLowMp = txtMp; changedSys = true; }
                }
                ImGui.Unindent(10f);
            }

            bool ccTrigger = configuration.TriggerLossOfControl; if (ImGui.Checkbox("Loss of Control Warning", ref ccTrigger)) { configuration.TriggerLossOfControl = ccTrigger; changedSys = true; }
            if (ccTrigger)
            {
                ImGui.Indent(10f);
                using (ImRaii.ItemWidth(150f))
                {
                    string txtCc = configuration.TriggerTextLossOfControl; if (ImGui.InputText("Text##cc", ref txtCc, 50)) { configuration.TriggerTextLossOfControl = txtCc; changedSys = true; }
                }
                ImGui.Unindent(10f);
            }
            if (changedSys) configuration.Save();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.1f, 1f), "Unified Aura System (WeakAuras)");
            ImGui.Text("Add a Buff ID, select target channels, and configure complex conditions.");
            ImGui.Separator();
            ImGui.Spacing();

            using (ImRaii.ItemWidth(200f))
            {
                ImGui.InputText("Search Buff/Debuff", ref searchInputTriggers, 100);
            }
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
                            var firstCh = configuration.Channels.FirstOrDefault()?.Name;
                            if (firstCh != null) newTrg.TargetChannels.Add(firstCh);

                            configuration.AuraTriggers.Insert(0, newTrg);
                            configuration.Save();
                        }
                    }
                }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.4f, 1f, 0.8f, 1f), "Import Aura");
            using (ImRaii.ItemWidth(300f))
            {
                ImGui.InputText("Aura Code", ref auraImportBuffer, 5000);
            }
            ImGui.SameLine();
            if (ImGui.Button("Add from Code##importAura"))
            {
                try
                {
                    string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auraImportBuffer));
                    var imported = Newtonsoft.Json.JsonConvert.DeserializeObject<AuraTrigger>(json);
                    if (imported != null) { configuration.AuraTriggers.Insert(0, imported); configuration.Save(); auraImportBuffer = ""; }
                }
                catch { }
            }

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            string[] channelNames = configuration.Channels.Select(c => c.Name).ToArray();
            using var aurasRegion = ImRaii.Child("AurasRegion", new Vector2(0, 400), true);
            if (aurasRegion)
            {
                for (int i = 0; i < configuration.AuraTriggers.Count; i++)
                {
                    var trg = configuration.AuraTriggers[i];
                    bool changedTrg = false;

                    using var blockId = ImRaii.PushId($"trg_block_{i}");
                    using var group = ImRaii.Group();

                    bool en = trg.Enabled;
                    if (ImGui.Checkbox("##en", ref en)) { trg.Enabled = en; changedTrg = true; }
                    ImGui.SameLine();

                    string name = plugin.Parser?.GetSkillName(trg.StatusId) ?? "Unknown";
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
                        configuration.AuraTriggers.RemoveAt(i);
                        configuration.Save();
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
                    using (ImRaii.ItemWidth(250f))
                    {
                        string msg = trg.CustomText;
                        if (ImGui.InputText("##txt", ref msg, 50)) { trg.CustomText = msg; changedTrg = true; }
                    }
                    DrawHelpMarker("Leave blank to display the original skill name.");

                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Sound on appear:"); ImGui.SameLine(200f);
                    using (ImRaii.ItemWidth(250f))
                    {
                        int snd = trg.SoundOverride;
                        if (ImGui.Combo("##snd", ref snd, TriggerSoundOptions, TriggerSoundOptions.Length)) { trg.SoundOverride = snd; changedTrg = true; }
                    }

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

                            using (ImRaii.ItemWidth(200f))
                            {
                                int cType = (int)cond.Type;
                                if (ImGui.Combo("##cType", ref cType, conditionTypes, conditionTypes.Length)) { cond.Type = (ConditionType)cType; changedTrg = true; }
                            }

                            if (cond.Type != ConditionType.None)
                            {
                                if (cond.Type == ConditionType.PlayerHP || cond.Type == ConditionType.TargetHP)
                                {
                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(100f))
                                    {
                                        int cOp = (int)cond.Operator;
                                        if (ImGui.Combo("##cOp", ref cOp, conditionOperators, conditionOperators.Length)) { cond.Operator = (ConditionOperator)cOp; changedTrg = true; }
                                    }

                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(100f))
                                    {
                                        float cVal = cond.Value;
                                        if (ImGui.DragFloat("##cVal", ref cVal, 1f, 0f, 100f, "%.1f%%")) { cond.Value = cVal; changedTrg = true; }
                                    }
                                }
                                else if (cond.Type == ConditionType.PlayerAuraStacks || cond.Type == ConditionType.TargetAuraStacks)
                                {
                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(80f))
                                    {
                                        int targetId = (int)cond.TargetStatusId;
                                        if (ImGui.InputInt("Buff ID##cTargetId", ref targetId, 0, 0)) { cond.TargetStatusId = (uint)Math.Max(0, targetId); changedTrg = true; }
                                    }

                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(80f))
                                    {
                                        int cOp = (int)cond.Operator;
                                        if (ImGui.Combo("##cOp", ref cOp, conditionOperators, conditionOperators.Length)) { cond.Operator = (ConditionOperator)cOp; changedTrg = true; }
                                    }

                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(80f))
                                    {
                                        int cVal = (int)cond.Value;
                                        if (ImGui.InputInt("Stacks##cVal", ref cVal, 0, 0)) { cond.Value = cVal; changedTrg = true; }
                                    }
                                }
                                else
                                {
                                    ImGui.SameLine();
                                    using (ImRaii.ItemWidth(150f))
                                    {
                                        int cValInt = (int)cond.Value;
                                        if (ImGui.InputInt("ID##cValID", ref cValInt, 0, 0)) { cond.Value = cValInt; changedTrg = true; }
                                    }
                                }
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("X##delCond")) { trg.Conditions.RemoveAt(c); changedTrg = true; break; }
                        }
                        ImGui.Unindent(10f);
                    }

                    ImGui.Unindent(30f);
                    ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

                    if (changedTrg) configuration.Save();
                }
            }
        }
    }
}
