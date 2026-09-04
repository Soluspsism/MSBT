using System.Numerics;

namespace MSBT;

internal sealed class CustomSCTNode
{
    public static ulong GlobalSpawnCounter = 0;
    public ulong SpawnId = 0;

    public bool IsActive = false;
    public bool IsFirstFrameTracker = true;

    public string Text = string.Empty;
    public string BaseText = string.Empty;
    public float Timer;

    public float DistanceTraveled = 0f;

    public float TargetXOffset;

    public bool IsCrit;
    public bool IsDirectHit;
    public bool IsBigHit = false;
    public bool IsHeal;
    public bool IsTextOnly;
    public bool IsMp;
    public bool IsFading;
    public bool IsAlert;
    public DisplayChannel Channel = null!;
    public uint IconId = 0;
    public int BaseValue;
    public int Hits;
    public uint MergeId;
    public string SkillName = string.Empty;
    public float CurrentX;
    public float CurrentY;

    public bool RequiresDurationCheck = false;
    public uint StatusId = 0;
    public uint TargetObjectId = 0;

    public float MaxDuration = 0f;
    public float RemainingTime = 0f;
    public int DmgType = 0;

    public void Init(string text, string baseText, float targetX,
        bool isCrit, bool isDirectHit, bool isHeal, bool isTextOnly, bool isMp, bool isAlert,
        DisplayChannel channel, uint iconId, int baseValue, uint mergeId, string skillName,
        bool requiresDurCheck, uint statusId, uint targetObjId, float maxDur, float remTime, int dmgType)
    {
        SpawnId = ++GlobalSpawnCounter;
        IsActive = true;
        IsFirstFrameTracker = true;

        Text = text;
        BaseText = baseText;
        Timer = 0f;
        DistanceTraveled = 0f;

        TargetXOffset = targetX;

        IsCrit = isCrit;
        IsDirectHit = isDirectHit;
        IsBigHit = false;
        IsHeal = isHeal;
        IsTextOnly = isTextOnly;
        IsMp = isMp;
        IsFading = false;
        IsAlert = isAlert;
        Channel = channel;
        IconId = iconId;
        BaseValue = baseValue;
        Hits = 1;
        MergeId = mergeId;
        SkillName = skillName;
        RequiresDurationCheck = requiresDurCheck;
        StatusId = statusId;
        TargetObjectId = targetObjId;
        MaxDuration = maxDur;
        RemainingTime = remTime;
        DmgType = dmgType;
    }

    public void Reset()
    {
        IsActive = false;
        Text = string.Empty;
        BaseText = string.Empty;
        SkillName = string.Empty;
        Channel = null!;
    }
}
