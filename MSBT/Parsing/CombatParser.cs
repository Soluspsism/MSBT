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

internal sealed unsafe partial class CombatParser : IDisposable
{
    [Flags]
    private enum FlyTextTraits : ushort
    {
        None = 0,
        Damage = 1 << 0,
        Heal = 1 << 1,
        Mp = 1 << 2,
        Status = 1 << 3,
        Fading = 1 << 4,
        Crit = 1 << 5,
        DirectHit = 1 << 6,
        InvulnerableOrMiss = 1 << 7,
        AutoAttack = 1 << 8,
    }

    private readonly struct HotDotContext
    {
        public readonly Character* Target;
        public readonly uint StatusId;
        public readonly int TickMode;
        public readonly uint Value;
        public readonly uint SourceEntityId;

        public HotDotContext(Character* target, uint statusId, int tickMode, uint value, uint sourceEntityId)
        {
            Target = target;
            StatusId = statusId;
            TickMode = tickMode;
            Value = value;
            SourceEntityId = sourceEntityId;
        }
    }

    private readonly Plugin plugin;
    private delegate void AddToScreenLogWithScreenLogKindDelegate(Character* target, Character* source, FlyTextKind logKind, byte option, byte actionKind, uint actionId, int value1, int value2, int value3);
    private delegate void ProcessHotDotDelegate(StatusManager* statusManager, Character* target, uint statusId, int tickMode, uint value, uint sourceEntityId, int damageType);
    private readonly Hook<AddToScreenLogWithScreenLogKindDelegate>? screenLogHook;
    private readonly Hook<ProcessHotDotDelegate>? processHotDotHook;

    [ThreadStatic]
    private static HotDotContext? ActiveHotDot;

    private static readonly long TriggerCooldownTicks = Stopwatch.Frequency * 5;
    private static readonly long AlertSoundCooldownTicks = Stopwatch.Frequency;
    private static readonly long CritSoundCooldownTicks = Stopwatch.Frequency / 2;

    private long lowHpTriggerTimestamp;
    private long lowMpTriggerTimestamp;
    private long lastCritSoundTimestamp;
    private long lastAlertSoundTimestamp;

    private readonly Dictionary<uint, (string Name, uint IconId, int DmgType)> actionCache = new();
    private readonly Dictionary<uint, (string Name, uint IconId)> statusCache = new();
    private readonly ExcelSheet<LuminaAction> actionSheet;
    private readonly ExcelSheet<LuminaStatus> statusSheet;
    private const int MaxCacheSize = 2000;

    public CombatParser(Plugin plugin)
    {
        this.plugin = plugin;
        actionSheet = Service.DataManager.GetExcelSheet<LuminaAction>();
        statusSheet = Service.DataManager.GetExcelSheet<LuminaStatus>();
        try
        {
            // TODO: Swap to FFXIVClientStruct AddToScreenLogWithScreenLogKind when it's merged in main Dalamud
            screenLogHook = Service.GameInteropProvider.HookFromSignature<AddToScreenLogWithScreenLogKindDelegate>(
                "E8 ?? ?? ?? ?? BF ?? ?? ?? ?? EB 39",
                AddToScreenLogWithScreenLogKindDetour);
            screenLogHook.Enable();
        }
        catch (Exception ex) { Service.Log.Error(ex, "Failed to hook AddToScreenLog"); }

        try
        {
            // TODO: Swap sig to FFXIVClientStruct ProcessHotDot when it's merged in main Dalamud
            processHotDotHook = Service.GameInteropProvider.HookFromSignature<ProcessHotDotDelegate>(
                "48 8B C4 48 89 58 ? 48 89 68 ? 48 89 70 ? 57 41 54 41 56 48 83 EC ? 4C 89 78",
                ProcessHotDotDetour);
            processHotDotHook.Enable();
        }
        catch (Exception ex) { Service.Log.Warning(ex, "Failed to hook ProcessHotDot"); }
    }

    public void Dispose()
    {
        processHotDotHook?.Dispose();
        screenLogHook?.Dispose();
    }

    private void ProcessHotDotDetour(StatusManager* statusManager, Character* target, uint statusId, int tickMode, uint value, uint sourceEntityId, int damageType)
    {
        HotDotContext? previous = ActiveHotDot;
        ActiveHotDot = new HotDotContext(target, statusId, tickMode, value, sourceEntityId);

        try
        {
            processHotDotHook!.Original(statusManager, target, statusId, tickMode, value, sourceEntityId, damageType);
        }
        finally
        {
            ActiveHotDot = previous;
        }
    }

    private void ManageCacheSize()
    {
        if (actionCache.Count > MaxCacheSize) actionCache.Clear();
        if (statusCache.Count > MaxCacheSize) statusCache.Clear();
    }

    private static FlyTextTraits GetFlyTextTraits(FlyTextKind kind)
    {
        const FlyTextTraits damage = FlyTextTraits.Damage;
        const FlyTextTraits autoAttack = damage | FlyTextTraits.AutoAttack;
        const FlyTextTraits status = FlyTextTraits.Status;
        const FlyTextTraits avoided = FlyTextTraits.InvulnerableOrMiss;

        return kind switch
        {
            FlyTextKind.AutoAttackOrDot => autoAttack,
            FlyTextKind.AutoAttackOrDotDh => autoAttack | FlyTextTraits.DirectHit,
            FlyTextKind.AutoAttackOrDotCrit => autoAttack | FlyTextTraits.Crit,
            FlyTextKind.AutoAttackOrDotCritDh => autoAttack | FlyTextTraits.Crit | FlyTextTraits.DirectHit,
            FlyTextKind.Damage => damage,
            FlyTextKind.DamageDh => damage | FlyTextTraits.DirectHit,
            FlyTextKind.DamageCrit => damage | FlyTextTraits.Crit,
            FlyTextKind.DamageCritDh => damage | FlyTextTraits.Crit | FlyTextTraits.DirectHit,
            FlyTextKind.Miss or FlyTextKind.NamedMiss or FlyTextKind.Dodge or FlyTextKind.NamedDodge => avoided,
            FlyTextKind.Buff or FlyTextKind.Debuff or FlyTextKind.DebuffNoEffect => status,
            FlyTextKind.BuffFading or FlyTextKind.DebuffFading => status | FlyTextTraits.Fading,
            FlyTextKind.DebuffResisted or FlyTextKind.DebuffInvulnerable => status | avoided,
            FlyTextKind.MpDrain or FlyTextKind.NamedTp or FlyTextKind.MpRegen or FlyTextKind.NamedTp2 or
                FlyTextKind.EpRegen or FlyTextKind.CpRegen or FlyTextKind.GpRegen or FlyTextKind.NamedMp3 or
                FlyTextKind.NamedTp3 => FlyTextTraits.Mp,
            FlyTextKind.NamedCriticalHitWithMp or FlyTextKind.NamedCriticalHitWithTp => FlyTextTraits.Mp | FlyTextTraits.Crit,
            FlyTextKind.Healing or FlyTextKind.HpDrain => FlyTextTraits.Heal,
            FlyTextKind.HealingCrit => FlyTextTraits.Heal | FlyTextTraits.Crit,
            FlyTextKind.Invulnerable or FlyTextKind.FullyResisted or FlyTextKind.Resist => avoided,
            _ => FlyTextTraits.None,
        };
    }

    private static bool TryStartCooldown(ref long timestamp, long currentTimestamp, long cooldownTicks)
    {
        if (timestamp != 0 && currentTimestamp - timestamp <= cooldownTicks)
            return false;

        timestamp = currentTimestamp;
        return true;
    }

    private AuraTrigger? FindActiveTrigger(uint statusId, DisplayChannel channel)
    {
        foreach (var trigger in plugin.Configuration.AuraTriggers)
        {
            if (trigger.Enabled &&
                trigger.StatusId == statusId &&
                (trigger.TargetChannels.Contains(channel.Name) || trigger.TargetChannelName == channel.Name))
                return trigger;
        }

        return null;
    }

    private CustomSCTNode? FindTrackerNode(DisplayChannel channel, uint statusId, uint targetId)
    {
        foreach (var node in plugin.CustomTexts)
        {
            if (node.IsActive && node.Channel == channel && node.StatusId == statusId && node.TargetObjectId == targetId)
                return node;
        }

        return null;
    }

    private CustomSCTNode? FindMergeNode(DisplayChannel channel, uint mergeId, bool isHeal, bool isMp, bool isCrit, bool isBigHit)
    {
        float throttleWindow = plugin.Configuration.ThrottleTimeWindow;
        foreach (var node in plugin.CustomTexts)
        {
            if (node.IsActive &&
                node.Channel == channel &&
                node.MergeId == mergeId &&
                node.IsHeal == isHeal &&
                node.IsMp == isMp &&
                node.IsCrit == isCrit &&
                node.IsBigHit == isBigHit &&
                node.Timer < throttleWindow)
                return node;
        }

        return null;
    }

    public string GetSkillName(uint id)
    {
        if (statusCache.TryGetValue(id, out var sc)) return sc.Name;
        try
        {
            if (statusSheet != null)
            {
                var row = statusSheet.GetRow(id);
                string name = row.Name.ToString();
                ManageCacheSize();
                statusCache[id] = (name, row.Icon);
                return name;
            }
        }
        catch { }
        return $"Unknown ({id})";
    }

    public uint GetIconId(uint id)
    {
        if (statusCache.TryGetValue(id, out var sc)) return sc.IconId;
        try
        {
            if (statusSheet != null)
            {
                var row = statusSheet.GetRow(id);
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
        abbr += words[^1];

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
                bool hasAura = false;
                foreach (var status in player.StatusList)
                {
                    if (status.StatusId != (uint)cond.Value) continue;
                    hasAura = true;
                    break;
                }
                if (cond.Type == ConditionType.PlayerHasAura && !hasAura) return false;
                if (cond.Type == ConditionType.PlayerMissingAura && hasAura) return false;
            }
            else if (cond.Type == ConditionType.TargetHasAura || cond.Type == ConditionType.TargetMissingAura)
            {
                if (target == null) return false;
                bool hasAura = false;
                foreach (var status in target.StatusList)
                {
                    if (status.StatusId != (uint)cond.Value) continue;
                    hasAura = true;
                    break;
                }
                if (cond.Type == ConditionType.TargetHasAura && !hasAura) return false;
                if (cond.Type == ConditionType.TargetMissingAura && hasAura) return false;
            }
            else if (cond.Type == ConditionType.PlayerAuraStacks)
            {
                if (player == null) return false;
                int stacks = 0;
                foreach (var status in player.StatusList)
                {
                    if (status.StatusId != cond.TargetStatusId) continue;
                    stacks = status.Param;
                    break;
                }

                if (cond.Operator == ConditionOperator.LessThan && !(stacks < cond.Value)) return false;
                if (cond.Operator == ConditionOperator.GreaterThan && !(stacks > cond.Value)) return false;
                if (cond.Operator == ConditionOperator.Equal && !(stacks == cond.Value)) return false;
            }
            else if (cond.Type == ConditionType.TargetAuraStacks)
            {
                if (target == null) return false;
                int stacks = 0;
                foreach (var status in target.StatusList)
                {
                    if (status.StatusId != cond.TargetStatusId) continue;
                    stacks = status.Param;
                    break;
                }

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

            lock (plugin.TextNodesGate)
            {
                float stackOffset = plugin.Renderer.GetSpawnOffset(ch, ch.NormalScale, false);

                var node = plugin.AcquireTextNode();
                node.Init(text ?? "", text ?? "", stackOffset, false, false, false, true, false, true, ch, 0, 0, uint.MaxValue, "", false, 0, 0, 0f, 0f, 0);
            }

            if (ch.AlertSound > 0)
            {
                long currentTimestamp = Stopwatch.GetTimestamp();
                if (TryStartCooldown(ref lastAlertSoundTimestamp, currentTimestamp, AlertSoundCooldownTicks))
                    plugin.Renderer.PlayInGameSound(ch.AlertSound);
            }
        }
    }
}
