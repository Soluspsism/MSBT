using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Game.Gui.FlyText;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace MSBT;

internal sealed unsafe partial class CombatParser
{
    private void AddToScreenLogWithScreenLogKindDetour(Character* target, Character* source, FlyTextKind logKind, byte option, byte actionKind, uint actionId, int value1, int value2, int value3)
    {
        try
        {
            var localPlayer = (Character*)(Service.ObjectTable.LocalPlayer?.Address ?? nint.Zero);
            if (localPlayer == null || target == null) goto OriginalCall;

            FlyTextTraits traits = GetFlyTextTraits(logKind);

            bool isDamage = (traits & FlyTextTraits.Damage) != 0;
            bool isHeal = (traits & FlyTextTraits.Heal) != 0;
            bool isMp = (traits & FlyTextTraits.Mp) != 0;

            bool isStatus = (traits & FlyTextTraits.Status) != 0;
            bool isFading = (traits & FlyTextTraits.Fading) != 0;
            bool isTextOnly = isStatus;

            bool isCrit = (traits & FlyTextTraits.Crit) != 0;
            bool isDirectHit = (traits & FlyTextTraits.DirectHit) != 0;
            bool isInvulnOrMiss = (traits & FlyTextTraits.InvulnerableOrMiss) != 0;

            if (isHeal && isMp) isHeal = false;

            bool isAutoAttackKind = (traits & FlyTextTraits.AutoAttack) != 0;
            bool isPeriodicDot = logKind == FlyTextKind.AutoAttackOrDot &&
                                 option == 0 &&
                                 actionKind == 0 &&
                                 actionId == 0 &&
                                 value1 > 0 &&
                                 value2 == 0 &&
                                 value3 is >= 0 and <= 3 &&
                                 target == source;
            HotDotContext? hotDot = ActiveHotDot;
            bool hasHotDotContext = isPeriodicDot &&
                                    hotDot is { TickMode: 3 } context &&
                                    context.Target == target &&
                                    context.Value == (uint)value1;

            int value = value1;
            if (value1 == 0 && !isDamage && !isInvulnOrMiss)
            {
                value = value2;
                if (value <= 0) value = value3;
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

            long currentTimestamp = Stopwatch.GetTimestamp();
            var localPlayerSafe = Service.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

            if (localPlayerSafe != null && (source == localPlayer || target == localPlayer))
            {
                if (plugin.Configuration.TriggerLowHp && localPlayerSafe.MaxHp > 0)
                {
                    float hpPercent = ((float)localPlayerSafe.CurrentHp / localPlayerSafe.MaxHp) * 100f;
                    if (hpPercent <= plugin.Configuration.LowHpThresholdPercent)
                    {
                        if (TryStartCooldown(ref lowHpTriggerTimestamp, currentTimestamp, TriggerCooldownTicks))
                            EnqueueSystemTrigger(plugin.Configuration.TriggerTextLowHp);
                    }
                    else if (hpPercent > plugin.Configuration.LowHpThresholdPercent) lowHpTriggerTimestamp = 0;
                }

                if (plugin.Configuration.TriggerLowMp && localPlayerSafe.MaxMp > 0)
                {
                    if (localPlayerSafe.CurrentMp <= plugin.Configuration.LowMpThresholdValue)
                    {
                        if (TryStartCooldown(ref lowMpTriggerTimestamp, currentTimestamp, TriggerCooldownTicks))
                            EnqueueSystemTrigger(plugin.Configuration.TriggerTextLowMp);
                    }
                    else if (localPlayerSafe.CurrentMp > plugin.Configuration.LowMpThresholdValue) lowMpTriggerTimestamp = 0;
                }
            }

            if (!isDamage && !isHeal && !isMp && !isStatus) goto OriginalCall;
            if (!isAbsorb && value <= 0 && (isDamage || isHeal || isMp)) goto OriginalCall;

            bool isMe = (target == localPlayer);
            bool isFromMe = false;

            if (source != null)
            {
                if (source == localPlayer) isFromMe = true;
                else if (source->GameObject.OwnerId == localPlayer->GameObject.EntityId) isFromMe = true;
            }
            if (!isFromMe && hasHotDotContext && hotDot!.Value.SourceEntityId == localPlayer->GameObject.EntityId)
                isFromMe = true;

            bool isMyTarget = Service.TargetManager.Target != null && Service.TargetManager.Target.Address == (nint)target;

            if (!isMe && !isFromMe && !isMyTarget) goto OriginalCall;
            if (isPeriodicDot && !isMe && !isFromMe) goto OriginalCall;

            Dalamud.Game.ClientState.Objects.Types.IBattleChara? dalTarget = null;
            if (isMe) dalTarget = localPlayerSafe as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
            else if (isMyTarget) dalTarget = Service.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

            bool isPureAutoAttack = actionId is 7 or 8 || (isAutoAttackKind && !isPeriodicDot && value2 == 0);

            uint currentSkillId = 0;
            if (hasHotDotContext)
            {
                currentSkillId = hotDot!.Value.StatusId;
            }
            else if (!isPureAutoAttack)
            {
                currentSkillId = isStatus ? (uint)value1 : actionId;
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
                if (isStatus || hasHotDotContext)
                {
                    if (statusCache.TryGetValue(currentSkillId, out var cached)) { skillName = cached.Name; iconId = cached.IconId; }
                    else
                    {
                        try
                        {
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
                if (isPeriodicDot) finalDmgType = Math.Max(1, value3);
                else if (option == 2) finalDmgType = 2;
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

            uint mergeId = isPureAutoAttack
                ? uint.MaxValue
                : isPeriodicDot && currentSkillId == 0
                    ? uint.MaxValue - 1
                    : currentSkillId;
            string rawSkillName = skillName ?? "";

            if (plugin.Configuration.DebugShowIds && currentSkillId > 0)
                rawSkillName = string.IsNullOrEmpty(rawSkillName) ? $"[ID:{currentSkillId}]" : $"[ID:{currentSkillId}] {rawSkillName}";

            foreach (var ch in plugin.Configuration.Channels)
            {
                if (!ch.Enabled) continue;
                if (isAbsorb && !ch.ShowAbsorbs) continue;

                bool isAlertEvent = false;
                string customMsg = "";
                int customSound = 0;

                if (currentSkillId > 0 && isStatus)
                {
                    var myTrigger = FindActiveTrigger(currentSkillId, ch);
                    if (myTrigger != null)
                    {
                        Dalamud.Game.ClientState.Objects.Types.IBattleChara? tTarget = Service.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
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
                    lock (plugin.TextNodesGate)
                    {
                        var existingTracker = FindTrackerNode(ch, currentSkillId, target->GameObject.EntityId);

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
                    lock (plugin.TextNodesGate)
                    {
                        var existingNode = FindMergeNode(ch, mergeId, isHeal, isMp, finalIsCrit, isBigHit);

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
                    if (finalAlertSound > 0 && TryStartCooldown(ref lastAlertSoundTimestamp, currentTimestamp, AlertSoundCooldownTicks))
                    {
                        plugin.Renderer.PlayInGameSound(finalAlertSound);
                    }
                }
                else if (finalIsCrit && ch.CritSound > 0)
                {
                    if (TryStartCooldown(ref lastCritSoundTimestamp, currentTimestamp, CritSoundCooldownTicks))
                        plugin.Renderer.PlayInGameSound(ch.CritSound);
                }

                lock (plugin.TextNodesGate)
                {
                    bool treatAsCritStream = finalIsCrit || (isBigHit && ch.BigHitActsAsCrit);
                    bool isCritStream = treatAsCritStream && ch.CritBehavior != 0 && !isAlertEvent && !isIncStatusEvent && !isOutStatusEvent;

                    float stackOffset = plugin.Renderer.GetSpawnOffsetAndBump(ch, scale, isCritStream);

                    bool needsDurationCheck = false;
                    if (isStatus && !isFading && currentSkillId > 0 && ch.ShowStatusDuration && statusDuration == 0f)
                        needsDurationCheck = true;

                    var node = plugin.AcquireTextNode();

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
        catch (Exception ex) { Service.Log.Error(ex, "Error in Detour"); }

    OriginalCall:
        screenLogHook!.Original(target, source, logKind, option, actionKind, actionId, value1, value2, value3);
    }
}
