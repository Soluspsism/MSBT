using Dalamud.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MSBT;

public enum ScrollDirection { Up, Down, Left, Right, Static, Pop, Fade }
public enum NumberFormatType { None, Space, Comma, Smart }
public enum TextAlignment { Left, Center, Right }
public enum TextEffectType { None, Shadow, Outline }

public enum ChannelMode { Scrolling, Tracker, Overlay }
public enum TrackerStyle { Text, IconOnly, IconDial, ProgressBar }

public enum ConditionType { None, PlayerHP, TargetHP, PlayerHasAura, PlayerMissingAura, TargetHasAura, TargetMissingAura, PlayerAuraStacks, TargetAuraStacks }
public enum ConditionOperator { LessThan, GreaterThan, Equal }

[Serializable]
public class TriggerCondition
{
    public ConditionType Type { get; set; } = ConditionType.None;
    public ConditionOperator Operator { get; set; } = ConditionOperator.LessThan;
    public float Value { get; set; } = 0f;
    public uint TargetStatusId { get; set; } = 0;
}

[Serializable]
public class AuraTrigger
{
    public bool Enabled { get; set; } = true;
    public uint StatusId { get; set; } = 0;
    public string CustomText { get; set; } = "";

    public string TargetChannelName { get; set; } = "";
    public List<string> TargetChannels { get; set; } = new List<string>();

    public bool OnlyCastByMe { get; set; } = true;
    public int SoundOverride { get; set; } = 0;

    public List<TriggerCondition> Conditions { get; set; } = new List<TriggerCondition>();
}

[Serializable]
public class DisplayChannel
{
    public string Name { get; set; } = "New Channel";
    public bool Enabled { get; set; } = true;
    public ChannelMode Mode { get; set; } = ChannelMode.Scrolling;
    public TrackerStyle TrackerStyle { get; set; } = TrackerStyle.Text;

    public bool AcceptsOutgoingDamage { get; set; } = false;
    public bool AcceptsIncomingDamage { get; set; } = false;
    public bool AcceptsOutgoingHeals { get; set; } = false;
    public bool AcceptsHeals { get; set; } = false;
    public bool AcceptsMp { get; set; } = false;
    public bool AcceptsStatuses { get; set; } = false;
    public bool AcceptsOutgoingStatuses { get; set; } = false;
    public bool AcceptsSystemAlerts { get; set; } = false;
    public bool CurrentTargetOnly { get; set; } = false;

    public bool ColorizeByType { get; set; } = false;

    public float X { get; set; } = 500f;
    public float Y { get; set; } = 500f;
    public float NormalScale { get; set; } = 0.5f;
    public float CritScale { get; set; } = 0.8f;
    public float IconScale { get; set; } = 1.0f;
    public float TrackerTimerScale { get; set; } = 0.8f;

    public string FontKey { get; set; } = "";
    public float FontSize { get; set; } = 0f;

    [JsonProperty("FontFileName")]
    private string LegacyFontFileName { set { if (string.IsNullOrWhiteSpace(FontKey)) FontKey = value ?? ""; } }
    public TextAlignment Alignment { get; set; } = TextAlignment.Center;

    public ScrollDirection Direction { get; set; } = ScrollDirection.Up;
    public float Curve { get; set; } = 0f;
    public float Speed { get; set; } = 60f;
    public float Duration { get; set; } = 1.5f;
    public float FadeDuration { get; set; } = 0.6f;

    public bool HideSkillNames { get; set; } = false;
    public bool HideIcons { get; set; } = false;
    public bool IconOnRight { get; set; } = false;
    public bool ShowStatusPrefixes { get; set; } = false;
    public bool ShowStatusDuration { get; set; } = false;

    public bool PulseEffect { get; set; } = false;
    public float PulseSpeed { get; set; } = 4.0f;
    public float PulseAmplitude { get; set; } = 0.1f;

    public int SpamThreshold { get; set; } = 0;

    public int CritBehavior { get; set; } = 1;
    public float CritLinger { get; set; } = 2.0f;
    public float CritDuration { get; set; } = 3.0f;
    public float CritOffsetX { get; set; } = 150f;
    public float CritOffsetY { get; set; } = -50f;
    public float CritCurvePhase { get; set; } = 0.5f;
    public float CritCurve { get; set; } = 0f;
    public float CritCurveStart { get; set; } = 0f;
    public float CritCurveEnd { get; set; } = 1f;

    public int CritSound { get; set; } = 0;
    public int AlertSound { get; set; } = 0;

    public bool AbbreviateSkillNames { get; set; } = false;
    public int MaxSkillNameLength { get; set; } = 15;

    public int BigHitThreshold { get; set; } = 0;
    public string BigHitPrefix { get; set; } = "";
    public string BigHitSuffix { get; set; } = "!!";
    public float BigHitScale { get; set; } = 1.3f;
    public bool BigHitActsAsCrit { get; set; } = true;
    public bool ColorizeBigHit { get; set; } = false;

    public int SmallHitThreshold { get; set; } = 0;
    public bool ShowAbsorbs { get; set; } = true;
    public string AbsorbText { get; set; } = "Block";
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public List<DisplayChannel> Channels { get; set; } = new List<DisplayChannel>();

    public TextEffectType TextEffect { get; set; } = TextEffectType.Outline;
    public float TextEffectSize { get; set; } = 2f;
    public NumberFormatType FormatType { get; set; } = NumberFormatType.Smart;
    public string FontKey { get; set; } = "";
    public float FontSize { get; set; } = 36f;

    [JsonProperty("EnableOutline")]
    private bool LegacyEnableOutline { set => TextEffect = value ? TextEffectType.Outline : TextEffectType.Shadow; }

    [JsonProperty("FontFileName")]
    private string LegacyFontFileName { set { if (string.IsNullOrWhiteSpace(FontKey)) FontKey = value ?? ""; } }

    public bool EnableThrottling { get; set; } = true;
    public float ThrottleTimeWindow { get; set; } = 0.5f;
    public bool DebugShowIds { get; set; } = false;

    public bool TriggerLowHp { get; set; } = false;
    public int LowHpThresholdPercent { get; set; } = 20;
    public string TriggerTextLowHp { get; set; } = "LOW HP!";
    public bool TriggerLowMp { get; set; } = false;
    public int LowMpThresholdValue { get; set; } = 2000;
    public string TriggerTextLowMp { get; set; } = "LOW MP!";
    public bool TriggerLossOfControl { get; set; } = false;
    public string TriggerTextLossOfControl { get; set; } = "LOSS OF CONTROL!";

    public List<uint> BlacklistedSkillIds { get; set; } = new List<uint>();
    public List<AuraTrigger> AuraTriggers { get; set; } = new List<AuraTrigger>();

    public Vector4 ColorZone1 { get; set; } = new Vector4(1f, 1f, 1f, 1f);
    public Vector4 ColorZone1Crit { get; set; } = new Vector4(1f, 0.8f, 0.2f, 1f);
    public Vector4 ColorZone2 { get; set; } = new Vector4(1f, 0.2f, 0.2f, 1f);

    public Vector4 ColorPhysical { get; set; } = new Vector4(1f, 0.6f, 0.2f, 1f);
    public Vector4 ColorMagical { get; set; } = new Vector4(0.2f, 0.8f, 1f, 1f);
    public Vector4 ColorUnique { get; set; } = new Vector4(0.8f, 0.2f, 1f, 1f);
    public Vector4 ColorBigHit { get; set; } = new Vector4(1f, 0.4f, 0.8f, 1f);

    public Vector4 ColorHeal { get; set; } = new Vector4(0.2f, 1f, 0.2f, 1f);
    public Vector4 ColorMp { get; set; } = new Vector4(0.9f, 0.5f, 0.9f, 1f);
    public Vector4 ColorStatus { get; set; } = new Vector4(0.8f, 0.4f, 1f, 1f);
    public Vector4 ColorStatusFading { get; set; } = new Vector4(0.6f, 0.6f, 0.6f, 1f);
    public Vector4 ColorZone4 { get; set; } = new Vector4(1f, 0.2f, 0.2f, 1f);
    public Vector4 ColorOutline { get; set; } = new Vector4(0f, 0f, 0f, 1f);

    public Dictionary<string, string> SavedPresets { get; set; } = new Dictionary<string, string>();
    public Dictionary<uint, string> ClassPresets { get; set; } = new Dictionary<uint, string>();
    public bool AutoSwitchPresets { get; set; } = false;

    public void EnsureInitialized(Vector2 viewportSize)
    {
        Version = 3;

        if (Channels.Count == 0)
        {
            float width = viewportSize.X > 0 ? viewportSize.X : 1920f;
            float height = viewportSize.Y > 0 ? viewportSize.Y : 1080f;
            Vector2 Position(float referenceX, float referenceY) => new(referenceX / 2560f * width, referenceY / 1440f * height);

            Vector2 outgoing = Position(1580f, 720f);
            Vector2 incoming = Position(980f, 720f);
            Vector2 statuses = Position(980f, 520f);
            Vector2 alerts = Position(1280f, 470f);
            Vector2 tracker = Position(1580f, 920f);
            Vector2 overlay = Position(1280f, 820f);

            Channels.Add(new DisplayChannel { Name = "Outgoing Damage", AcceptsOutgoingDamage = true, X = outgoing.X, Y = outgoing.Y, Direction = ScrollDirection.Up });
            Channels.Add(new DisplayChannel { Name = "Incoming & Healing", AcceptsIncomingDamage = true, AcceptsHeals = true, AcceptsMp = true, X = incoming.X, Y = incoming.Y, Direction = ScrollDirection.Down, CritOffsetX = -150f });
            Channels.Add(new DisplayChannel { Name = "Statuses", AcceptsStatuses = true, X = statuses.X, Y = statuses.Y, Direction = ScrollDirection.Static, CritBehavior = 0 });
            Channels.Add(new DisplayChannel { Name = "Alerts & Triggers", AcceptsSystemAlerts = true, X = alerts.X, Y = alerts.Y, Direction = ScrollDirection.Static, NormalScale = 1.5f, CritScale = 1.5f, CritBehavior = 0, ShowStatusPrefixes = true });
            Channels.Add(new DisplayChannel { Name = "Debuff Tracker", Mode = ChannelMode.Tracker, TrackerStyle = TrackerStyle.IconDial, AcceptsOutgoingStatuses = true, CurrentTargetOnly = true, X = tracker.X, Y = tracker.Y, ShowStatusDuration = true, Direction = ScrollDirection.Right });
            Channels.Add(new DisplayChannel { Name = "Graphical Overlay", Mode = ChannelMode.Overlay, X = overlay.X, Y = overlay.Y, NormalScale = 2.0f, IconScale = 1.5f, Direction = ScrollDirection.Static });
        }
    }

    public void Save() => ConfigRepository.Save(this);

    public string ExportToBase64()
    {
        try { var clone = (Configuration)MemberwiseClone(); clone.SavedPresets = new Dictionary<string, string>(); clone.ClassPresets = new Dictionary<uint, string>(); string json = JsonConvert.SerializeObject(clone, Formatting.None); return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)); } catch { return ""; }
    }

    public bool ImportFromBase64(string base64)
    {
        try
        {
            var backupPresets = new Dictionary<string, string>(SavedPresets ?? new());
            var backupClasses = new Dictionary<uint, string>(ClassPresets ?? new());
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            JsonConvert.PopulateObject(json, this);
            SavedPresets = backupPresets;
            ClassPresets = backupClasses;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
