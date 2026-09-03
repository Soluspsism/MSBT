using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Hooking;
using Dalamud.Game.Gui.FlyText;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Dalamud.Bindings.ImGui;

namespace MSBT;

public sealed unsafe class CombatParser : IDisposable
{
    private readonly Plugin plugin;
    private delegate void AddToScreenLogWithScreenLogKindDelegate(Character* target, Character* source, FlyTextKind logKind, byte option, byte actionKind, int actionId, int val1, int val2, int val3);
    private readonly Hook<AddToScreenLogWithScreenLogKindDelegate>? hook;

    private Dictionary<string, float> triggerCooldowns = new Dictionary<string, float>();
    private float lastCritSoundTime = 0f;
    private float lastAlertSoundTime = 0f;

    private readonly Dictionary<uint, (string Name, uint IconId, int DmgType)> actionCache = new();
    private readonly Dictionary<uint, (string Name, uint IconId)> statusCache = new();
    private const int MaxCacheSize = 2000;

    public CombatParser(Plugin plugin)
    {
        this.plugin = plugin;
        try
        {
            nint address = Plugin.SigScanner.ScanText("E8 ?? ?? ?? ?? BF ?? ?? ?? ?? EB 39");
            hook = Plugin.GameInteropProvider.HookFromAddress<AddToScreenLogWithScreenLogKindDelegate>(address, AddToScreenLogWithScreenLogKindDetour);
            hook.Enable();
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "Failed to hook AddToScreenLog"); }
    }

    public void Dispose()
    {
        hook?.Dispose();
    }

    private void ManageCacheSize()
    {
        if (actionCache.Count > MaxCacheSize) actionCache.Clear();
        if (statusCache.Count > MaxCacheSize) statusCache.Clear();
    }

    public string GetSkillName(uint id)
    {
        if (statusCache.TryGetValue(id, out var sc)) return sc.Name;
        if (actionCache.TryGetValue(id, out var ac)) return ac.Name;
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet != null)
            {
                var row = sheet.GetRow(id);
                ManageCacheSize();
                statusCache[id] = (row.Name.ToString(), row.Icon);
                return row.Name.ToString();
            }
        }
        catch { }
        return $"Unknown ({id})";
    }

    public uint GetIconId(uint id)
    {
        if (statusCache.TryGetValue(id, out var sc)) return sc.IconId;
        if (actionCache.TryGetValue(id, out var ac)) return ac.IconId;
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (sheet != null)
            {
                var row = sheet.GetRow(id);
                ManageCacheSize();
                statusCache[id] = (row.Name.ToString(), row.Icon);
                return row.Icon;
            }
        }
        catch { }
        return 0;
    }

    private string FormatSkillName(string rawName, DisplayChannel ch)
    {
        if (string.IsNullOrEmpty(rawName) || !ch.AbbreviateSkillNames) return rawName;
        if (rawName.Length <= ch.MaxSkillNameLength) return rawName;

        var words = rawName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return rawName.Substring(0, ch.MaxSkillNameLength) + "...";

        string abbr = "";
        for (int i = 0; i < words.Length - 1; i++)
        {
            if (words[i].Length > 0 && char.IsLetterOrDigit(words[i][0]))
                abbr += words[i][0] + ". ";
            else
                abbr += words[i] + " ";
        }
        abbr += words.Last();

        if (abbr.Length > ch.MaxSkillNameLength) return abbr.Substring(0, ch.MaxSkillNameLength) + "...";
        return abbr;
    }

    public bool CheckConditions(List<TriggerCondition> conditions, Dalamud.Game.ClientState.Objects.Types.IBattleChara? player, Dalamud.Game.ClientState.Objects.Types.IBattleChara? target)
    {
        if (conditions == null || conditions.Count == 0) return true;

        foreach (var cond in conditions)
        {
            if (cond.Type == ConditionType.None) continue;

            if (cond.Type == ConditionType.PlayerHP)
            {
                if (player == null) return false;
                float hpPct = ((float)player.CurrentHp / player.MaxHp) * 100f;
                if (cond.Operator == ConditionOperator.LessThan && hpPct >= cond.Value) return false;
                if (cond.Operator == ConditionOperator.GreaterThan && hpPct <= cond.Value) return false;
                if (cond.Operator == ConditionOperator.Equal && Math.Abs(hpPct - cond.Value) > 0.1f) return false;
            }
            else if (cond.Type == ConditionType.TargetHP)
            {
                if (target == null) return false;
                float hpPct = ((float)target.CurrentHp / target.MaxHp) * 100f;
                if (cond.Operator == ConditionOperator.LessThan && hpPct >= cond.Value) return false;
                if (cond.Operator == ConditionOperator.GreaterThan && hpPct <= cond.Value) return false;
                if (cond.Operator == ConditionOperator.Equal && Math.Abs(hpPct - cond.Value) > 0.1f) return false;
            }
            else if (cond.Type == ConditionType.PlayerHasAura || cond.Type == ConditionType.PlayerMissingAura)
            {
                if (player == null) return false;
                bool hasAura = player.StatusList.Any(s => s.StatusId == (uint)cond.Value);
                if (cond.Type == ConditionType.PlayerHasAura && !hasAura) return false;
                if (cond.Type == ConditionType.PlayerMissingAura && hasAura) return false;
            }
            else if (cond.Type == ConditionType.TargetHasAura || cond.Type == ConditionType.TargetMissingAura)
            {
                if (target == null) return false;
                bool hasAura = target.StatusList.Any(s => s.StatusId == (uint)cond.Value);
                if (cond.Type == ConditionType.TargetHasAura && !hasAura) return false;
                if (cond.Type == ConditionType.TargetMissingAura && hasAura) return false;
            }
            else if (cond.Type == ConditionType.PlayerAuraStacks)
            {
                if (player == null) return false;
                var aura = player.StatusList.FirstOrDefault(s => s.StatusId == cond.TargetStatusId);
                int stacks = aura != null ? aura.Param : 0;

                if (cond.Operator == ConditionOperator.LessThan && !(stacks < cond.Value)) return false;
                if (cond.Operator == ConditionOperator.GreaterThan && !(stacks > cond.Value)) return false;
                if (cond.Operator == ConditionOperator.Equal && !(stacks == cond.Value)) return false;
            }
            else if (cond.Type == ConditionType.TargetAuraStacks)
            {
                if (target == null) return false;
                var aura = target.StatusList.FirstOrDefault(s => s.StatusId == cond.TargetStatusId);
                int stacks = aura != null ? aura.Param : 0;

                if (cond.Operator == ConditionOperator.LessThan && !(stacks < cond.Value)) return false;
                if (cond.Operator == ConditionOperator.GreaterThan && !(stacks > cond.Value)) return false;
                if (cond.Operator == ConditionOperator.Equal && !(stacks == cond.Value)) return false;
            }
        }
        return true;
    }

    private void EnqueueSystemTrigger(string text)
    {
        foreach (var ch in plugin.Configuration.Channels)
        {
            if (!ch.Enabled || !ch.AcceptsSystemAlerts) continue;

            float scale = ch.NormalScale;
            float stackOffset = plugin.Renderer.GetSpawnOffsetAndBump(ch, scale, ch.Direction, false);

            float tX = 0f; float tY = 0f;
            if (ch.Direction == ScrollDirection.Left || ch.Direction == ScrollDirection.Right) tX = stackOffset;
            else tY = stackOffset;

            lock (plugin.CustomTexts)
            {
                var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
                if (node == null) { node = new CustomSCTNode(); plugin.CustomTexts.Add(node); }
                node.Init(text ?? "", text ?? "", tY, tX, false, false, false, true, false, true, ch, 0, 0, uint.MaxValue, "", false, 0, 0, 0f, 0f, 0);
            }

            if (ch.AlertSound > 0)
            {
                float currentTime = (float)ImGui.GetTime();
                if (currentTime - lastAlertSoundTime > 1.0f) { lastAlertSoundTime = currentTime; plugin.Renderer.PlayInGameSound(ch.AlertSound); }
            }
        }
    }

    private void AddToScreenLogWithScreenLogKindDetour(Character* target, Character* source, FlyTextKind logKind, byte option, byte actionKind, int actionId, int val1, int val2, int val3)
    {
        try
        {
            var localPlayer = (Character*)(Plugin.ObjectTable.LocalPlayer?.Address ?? nint.Zero);
            if (localPlayer == null || target == null) goto OriginalCall;

            string kindStr = logKind.ToString().ToLowerInvariant();

            bool isDamage = kindStr.Contains("damage") || kindStr.Contains("autoattack") || kindStr.Contains("dot");
            bool isHeal = kindStr.Contains("healing") || kindStr.Contains("heal") || kindStr.Contains("drain") || kindStr.Contains("absorb");
            bool isMp = kindStr.Contains("mp") || kindStr.Contains("cp") || kindStr.Contains("gp") || kindStr.Contains("ep") || kindStr.Contains("tp");

            bool isStatus = kindStr.Contains("buff") || kindStr.Contains("debuff") || kindStr.Contains("fading") || kindStr.Contains("namedicon") || kindStr.Contains("status") || kindStr.Contains("aura");
            bool isAction = kindStr.Contains("action");
            bool isFading = kindStr.Contains("fading");
            bool isTextOnly = isStatus || isAction;

            bool isCrit = kindStr.Contains("crit") || kindStr.Contains("cdh");
            bool isDirectHit = kindStr.Contains("direct") || kindStr.Contains("dh") || kindStr.Contains("cdh");
            bool isInvulnOrMiss = kindStr.Contains("invuln") || kindStr.Contains("evade") || kindStr.Contains("miss") || kindStr.Contains("resist") || kindStr.Contains("block") || kindStr.Contains("parry");

            if (isHeal && isMp) isHeal = false;

            bool isAutoAttackKind = kindStr.Contains("autoattack");
            bool isDot = isAutoAttackKind && val2 > 0;

            int value = val1;
            if (isDot)
            {
                value = val2;
            }
            else if (val1 == 0 && !isDamage && !isInvulnOrMiss)
            {
                value = val2;
                if (value <= 0) value = val3;
            }

            bool isAbsorb = false;

            if (isDamage && value <= 0)
            {
                isAbsorb = true;
                value = 0;
            }
            else if (isInvulnOrMiss)
            {
                isDamage = true;
                isAbsorb = true;
                value = 0;
            }

            float currentTime = (float)ImGui.GetTime();
            var localPlayerSafe = Plugin.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

            if (localPlayerSafe != null && (source == localPlayer || target == localPlayer))
            {
                if (plugin.Configuration.TriggerLowHp && localPlayerSafe.MaxHp > 0)
                {
                    float hpPercent = ((float)localPlayerSafe.CurrentHp / localPlayerSafe.MaxHp) * 100f;
                    if (hpPercent <= plugin.Configuration.LowHpThresholdPercent) { if (!triggerCooldowns.ContainsKey("LowHP") || (currentTime - triggerCooldowns["LowHP"]) > 5.0f) { triggerCooldowns["LowHP"] = currentTime; EnqueueSystemTrigger(plugin.Configuration.TriggerTextLowHp); } }
                    else if (hpPercent > plugin.Configuration.LowHpThresholdPercent) { triggerCooldowns["LowHP"] = 0f; }
                }

                if (plugin.Configuration.TriggerLowMp && localPlayerSafe.MaxMp > 0)
                {
                    if (localPlayerSafe.CurrentMp <= plugin.Configuration.LowMpThresholdValue) { if (!triggerCooldowns.ContainsKey("LowMP") || (currentTime - triggerCooldowns["LowMP"]) > 5.0f) { triggerCooldowns["LowMP"] = currentTime; EnqueueSystemTrigger(plugin.Configuration.TriggerTextLowMp); } }
                    else if (localPlayerSafe.CurrentMp > plugin.Configuration.LowMpThresholdValue) { triggerCooldowns["LowMP"] = 0f; }
                }
            }

            if (!isDamage && !isHeal && !isMp && !isStatus && !isAction) goto OriginalCall;
            if (!isAbsorb && value <= 0 && (isDamage || isHeal || isMp)) goto OriginalCall;

            bool isMe = (target == localPlayer);
            bool isFromMe = false;

            if (source != null)
            {
                if (source == localPlayer) isFromMe = true;
                else if (source->GameObject.OwnerId == localPlayer->GameObject.EntityId) isFromMe = true;
            }

            bool isMyTarget = Plugin.TargetManager.Target != null && Plugin.TargetManager.Target.Address == (nint)target;

            if (!isMe && !isFromMe && !isMyTarget) goto OriginalCall;

            Dalamud.Game.ClientState.Objects.Types.IBattleChara? dalTarget = null;
            if (isMe) dalTarget = localPlayerSafe as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
            else if (isMyTarget) dalTarget = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

            bool isPureAutoAttack = (actionId == 7 || actionId == 8) || (isAutoAttackKind && val2 == 0);

            uint currentSkillId = 0;
            if (!isPureAutoAttack)
            {
                currentSkillId = (uint)(isStatus || isDot ? val1 : actionId);
            }

            if (currentSkillId > 0 && plugin.Configuration.BlacklistedSkillIds.Contains(currentSkillId)) goto OriginalCall;

            bool isOutDmgEvent = isFromMe && !isMe && isDamage;
            bool isIncDmgEvent = isMe && isDamage;
            bool isOutHealEvent = isFromMe && isHeal;
            bool isIncHealEvent = isMe && isHeal;
            bool isMpEvent = isMe && isMp;
            bool isIncStatusEvent = isMe && isTextOnly;
            bool isOutStatusEvent = isFromMe && !isMe && isTextOnly && (int)target->GameObject.ObjectKind != 1;

            int cachedDmgType = 1;
            string skillName = ""; uint iconId = 0;

            if (!isPureAutoAttack && currentSkillId > 0)
            {
                if (isStatus || isDot)
                {
                    if (statusCache.TryGetValue(currentSkillId, out var cached)) { skillName = cached.Name; iconId = cached.IconId; }
                    else
                    {
                        try
                        {
                            var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
                            if (statusSheet != null)
                            {
                                var row = statusSheet.GetRow(currentSkillId); skillName = row.Name.ToString(); iconId = row.Icon;

                                if ((iconId == 0 || iconId == 405) && !string.IsNullOrEmpty(skillName))
                                {
                                    foreach (var stat in statusSheet) if (stat.Name.ToString().Equals(skillName, StringComparison.OrdinalIgnoreCase) && stat.Icon > 0 && stat.Icon != 405) { iconId = stat.Icon; break; }
                                }
                                ManageCacheSize();
                                statusCache[currentSkillId] = (skillName, iconId);
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    if (actionCache.TryGetValue(currentSkillId, out var cached)) { skillName = cached.Name; iconId = cached.IconId; cachedDmgType = cached.DmgType; }
                    else
                    {
                        try
                        {
                            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                            if (actionSheet != null)
                            {
                                var row = actionSheet.GetRow(currentSkillId); skillName = row.Name.ToString(); iconId = row.Icon;
                                if (row.AttackType.RowId == 5 || row.ActionCategory.RowId == 2) cachedDmgType = 2;
                                else if (row.AttackType.RowId >= 6) cachedDmgType = 3;

                                if ((iconId == 0 || iconId == 405) && !string.IsNullOrEmpty(skillName))
                                {
                                    foreach (var act in actionSheet)
                                    {
                                        if (act.Name.ToString().Equals(skillName, StringComparison.OrdinalIgnoreCase) && act.Icon > 0 && act.Icon != 405)
                                        {
                                            iconId = act.Icon;
                                            break;
                                        }
                                    }
                                }
                                ManageCacheSize();
                                actionCache[currentSkillId] = (skillName, iconId, cachedDmgType);
                            }
                        }
                        catch { }
                    }
                }
            }

            int finalDmgType = 1;
            if (isDamage)
            {
                if (option == 2) finalDmgType = 2;
                else if (option == 3 || option == 4) finalDmgType = 3;
                else if (cachedDmgType > 1) finalDmgType = cachedDmgType;
            }

            float statusDuration = 0f;
            if (isStatus && !isFading && currentSkillId > 0 && dalTarget != null)
            {
                foreach (var status in dalTarget.StatusList)
                {
                    if (status.StatusId == currentSkillId) { statusDuration = status.RemainingTime; break; }
                }
            }

            uint mergeId = isPureAutoAttack ? uint.MaxValue : currentSkillId;
            string rawSkillName = skillName ?? "";

            if (plugin.Configuration.DebugShowIds && currentSkillId > 0)
                rawSkillName = string.IsNullOrEmpty(rawSkillName) ? $"[ID:{currentSkillId}]" : $"[ID:{currentSkillId}] {rawSkillName}";

            var activeTriggers = plugin.Configuration.AuraTriggers.Where(t => t.Enabled && t.StatusId == currentSkillId).ToList();

            foreach (var ch in plugin.Configuration.Channels)
            {
                if (!ch.Enabled) continue;
                if (isAbsorb && !ch.ShowAbsorbs) continue;

                bool isAlertEvent = false;
                string customMsg = "";
                int customSound = 0;

                if (currentSkillId > 0 && isStatus)
                {
                    var myTrigger = activeTriggers.FirstOrDefault(t => t.TargetChannels.Contains(ch.Name) || t.TargetChannelName == ch.Name);
                    if (myTrigger != null)
                    {
                        Dalamud.Game.ClientState.Objects.Types.IBattleChara? tTarget = Plugin.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
                        if (CheckConditions(myTrigger.Conditions, localPlayerSafe, tTarget))
                        {
                            if (myTrigger.OnlyCastByMe && !isFromMe) { }
                            else if (isMe || isMyTarget || (isFromMe && !isMe))
                            {
                                isAlertEvent = true;
                                customMsg = myTrigger.CustomText ?? "";
                                customSound = myTrigger.SoundOverride;
                            }
                        }
                    }
                }

                if (ch.Mode == ChannelMode.Tracker && !isStatus) continue;
                if (ch.Mode == ChannelMode.Overlay && !isAlertEvent) continue;
                if (ch.CurrentTargetOnly && !isMe && !isMyTarget) continue;

                bool accept = false;
                if (isOutDmgEvent && ch.AcceptsOutgoingDamage) accept = true;
                if (isIncDmgEvent && ch.AcceptsIncomingDamage) accept = true;
                if (isOutHealEvent && ch.AcceptsOutgoingHeals) accept = true;
                if (isIncHealEvent && ch.AcceptsHeals) accept = true;
                if (isMpEvent && ch.AcceptsMp) accept = true;
                if (isIncStatusEvent && ch.AcceptsStatuses) accept = true;
                if (isOutStatusEvent && ch.AcceptsOutgoingStatuses) accept = true;
                if (isAlertEvent) accept = true;

                if (!accept) continue;
                if ((isOutDmgEvent || isIncDmgEvent) && !isAbsorb && value < ch.SpamThreshold) continue;

                bool hideName = ch.HideSkillNames;
                bool hideIcon = ch.HideIcons || plugin.Configuration.DebugShowIds;

                string formattedName = hideName ? "" : FormatSkillName(rawSkillName, ch) ?? "";
                uint finalIcon = hideIcon ? 0 : iconId;

                bool finalIsCrit = isCrit;
                bool finalIsDH = isDirectHit;
                bool isBigHit = ch.BigHitThreshold > 0 && value >= ch.BigHitThreshold && (isDamage || isHeal);

                if (ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay)
                {
                    lock (plugin.CustomTexts)
                    {
                        var existingTracker = plugin.CustomTexts.FirstOrDefault(x =>
                            x.IsActive && x.Channel == ch && x.StatusId == currentSkillId && x.TargetObjectId == target->GameObject.EntityId);

                        if (existingTracker != null)
                        {
                            existingTracker.Timer = 0f;
                            existingTracker.RequiresDurationCheck = true;
                            existingTracker.IsFading = false;
                            existingTracker.MaxDuration = 0f;
                            continue;
                        }
                    }
                }

                if (plugin.Configuration.EnableThrottling && !isTextOnly && !isAlertEvent && !isAbsorb && ch.Mode == ChannelMode.Scrolling)
                {
                    lock (plugin.CustomTexts)
                    {
                        var existingNode = plugin.CustomTexts.FirstOrDefault(x =>
                            x.IsActive && x.Channel == ch && x.MergeId == mergeId && x.IsHeal == isHeal && x.IsMp == isMp && x.IsCrit == finalIsCrit && x.IsBigHit == isBigHit && x.Timer < plugin.Configuration.ThrottleTimeWindow
                        );

                        if (existingNode != null)
                        {
                            existingNode.BaseValue += value;
                            existingNode.Hits++;

                            if (finalIsDH) existingNode.IsDirectHit = true;

                            string numStr = plugin.Renderer.FormatNumber(existingNode.BaseValue, plugin.Configuration.FormatType);
                            string baseText = "";

                            if (isBigHit)
                            {
                                string prefix = ch.BigHitPrefix ?? "";
                                string suffix = ch.BigHitSuffix ?? "";
                                baseText = ch.IconOnRight ? ($"x{existingNode.Hits} {prefix}{numStr}{suffix}") : ($"{prefix}{numStr}{suffix} x{existingNode.Hits}");
                            }
                            else
                            {
                                string marks = (existingNode.IsCrit && existingNode.IsDirectHit) ? "!!" : (existingNode.IsCrit ? "!" : (existingNode.IsDirectHit ? "*" : ""));
                                baseText = ch.IconOnRight ? ($"x{existingNode.Hits} " + marks + numStr) : (numStr + marks + $" x{existingNode.Hits}");
                            }

                            string textFull = !string.IsNullOrEmpty(existingNode.SkillName) ? (ch.IconOnRight ? $"{baseText} {existingNode.SkillName}" : $"{existingNode.SkillName} {baseText}") : baseText;

                            existingNode.Text = textFull ?? "";
                            existingNode.BaseText = textFull ?? "";
                            existingNode.Timer = Math.Max(0, existingNode.Timer - 0.1f);
                            continue;
                        }
                    }
                }

                string finalDamageText = "";
                string baseDamageText = "";

                if (isAlertEvent && !string.IsNullOrWhiteSpace(customMsg))
                {
                    finalDamageText = customMsg;
                    baseDamageText = customMsg;
                }
                else if (isTextOnly)
                {
                    baseDamageText = formattedName;
                    finalDamageText = baseDamageText;

                    if (isStatus && !string.IsNullOrEmpty(finalDamageText))
                    {
                        if (ch.ShowStatusPrefixes)
                        {
                            finalDamageText = ch.IconOnRight ? finalDamageText + (isFading ? " -" : " +") : (isFading ? "- " : "+ ") + finalDamageText;
                            baseDamageText = finalDamageText;
                        }
                        if (ch.ShowStatusDuration && !isFading && statusDuration > 0 && statusDuration < 9000f && ch.Mode == ChannelMode.Scrolling)
                        {
                            string durStr = statusDuration >= 60f ? $"{(int)(statusDuration / 60)}m {(int)(statusDuration % 60)}s" : $"{statusDuration:F0}s";
                            finalDamageText = ch.IconOnRight ? $"({durStr}) {finalDamageText}" : $"{finalDamageText} ({durStr})";
                        }
                    }
                }
                else if (isAbsorb)
                {
                    string safeAbsorbText = string.IsNullOrEmpty(ch.AbsorbText) ? "Block" : ch.AbsorbText;
                    baseDamageText = safeAbsorbText;
                    if (!string.IsNullOrEmpty(formattedName))
                    {
                        baseDamageText = ch.IconOnRight ? $"{safeAbsorbText} {formattedName}".Trim() : $"{formattedName} {safeAbsorbText}".Trim();
                    }
                    finalDamageText = baseDamageText;
                }
                else
                {
                    string numStr = plugin.Renderer.FormatNumber(value, plugin.Configuration.FormatType);

                    if (isBigHit)
                    {
                        string prefix = ch.BigHitPrefix ?? "";
                        string suffix = ch.BigHitSuffix ?? "";
                        baseDamageText = $"{prefix}{numStr}{suffix}";
                    }
                    else
                    {
                        string marks = (finalIsCrit && finalIsDH) ? "!!" : (finalIsCrit ? "!" : (finalIsDH ? "*" : ""));
                        baseDamageText = ch.IconOnRight ? (marks + numStr) : (numStr + marks);
                    }

                    if (!string.IsNullOrEmpty(formattedName)) { baseDamageText = ch.IconOnRight ? $"{baseDamageText} {formattedName}".Trim() : $"{formattedName} {baseDamageText}".Trim(); }
                    finalDamageText = baseDamageText;
                }

                float scale = finalIsCrit ? ch.CritScale : ch.NormalScale;
                if (isBigHit && !finalIsCrit) scale = ch.BigHitScale;

                if (isAlertEvent)
                {
                    int finalAlertSound = customSound > 0 ? customSound : ch.AlertSound;
                    if (finalAlertSound > 0 && currentTime - lastAlertSoundTime > 1.0f)
                    {
                        lastAlertSoundTime = currentTime;
                        plugin.Renderer.PlayInGameSound(finalAlertSound);
                    }
                }
                else if (finalIsCrit && ch.CritSound > 0)
                {
                    if (currentTime - lastCritSoundTime > 0.5f) { lastCritSoundTime = currentTime; plugin.Renderer.PlayInGameSound(ch.CritSound); }
                }

                lock (plugin.CustomTexts)
                {
                    bool treatAsCritStream = finalIsCrit || (isBigHit && ch.BigHitActsAsCrit);
                    bool isCritStream = treatAsCritStream && ch.CritBehavior != 0 && !isAlertEvent && !isIncStatusEvent && !isOutStatusEvent;

                    float stackOffset = plugin.Renderer.GetSpawnOffsetAndBump(ch, scale, ch.Direction, isCritStream);

                    bool needsDurationCheck = false;
                    if (isStatus && !isFading && currentSkillId > 0 && ch.ShowStatusDuration && statusDuration == 0f)
                        needsDurationCheck = true;

                    var node = plugin.CustomTexts.FirstOrDefault(n => !n.IsActive);
                    if (node == null)
                    {
                        node = new CustomSCTNode();
                        plugin.CustomTexts.Add(node);
                    }

                    finalDamageText ??= "";
                    baseDamageText ??= "";
                    formattedName ??= "";

                    node.Init(finalDamageText, baseDamageText, 0f, stackOffset, finalIsCrit, finalIsDH, isHeal,
                              isTextOnly, isMpEvent, isAlertEvent, ch, finalIcon, value, mergeId, formattedName,
                              needsDurationCheck, currentSkillId, target->GameObject.EntityId, 0f, 0f, finalDmgType);

                    node.IsBigHit = isBigHit;
                    if (isTextOnly && isFading) node.IsFading = true;
                }
            }
            return;
        }
        catch (Exception ex) { Plugin.Log.Error(ex, "Error in Detour"); }

    OriginalCall:
        hook!.Original(target, source, logKind, option, actionKind, actionId, val1, val2, val3);
    }
}
