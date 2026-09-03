using System.Numerics;

namespace MSBT;

public class CustomSCTNode
{
    public static ulong GlobalSpawnCounter = 0;
    public ulong SpawnId = 0;

    public bool IsActive = false;
    public bool IsFirstFrameTracker = true;

    public string Text = string.Empty;
    public string BaseText = string.Empty;
    public float Timer;

    public float DistanceTraveled = 0f;

    public float TargetYOffset;
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

    public void Init(string text, string baseText, float targetY, float targetX,
        bool isCrit, bool isDirectHit, bool isHeal, bool isTextOnly, bool isMp, bool isAlert,
        DisplayChannel channel, uint iconId, int baseValue, uint mergeId, string skillName,
        bool requiresDurCheck, uint statusId, uint targetObjId, float maxDur, float remTime, int dmgType)
    {
        this.SpawnId = ++GlobalSpawnCounter;
        this.IsActive = true;
        this.IsFirstFrameTracker = true;

        this.Text = text;
        this.BaseText = baseText;
        this.Timer = 0f;
        this.DistanceTraveled = 0f;

        this.TargetYOffset = targetY;
        this.TargetXOffset = targetX;

        this.IsCrit = isCrit;
        this.IsDirectHit = isDirectHit;
        this.IsBigHit = false;
        this.IsHeal = isHeal;
        this.IsTextOnly = isTextOnly;
        this.IsMp = isMp;
        this.IsFading = false;
        this.IsAlert = isAlert;
        this.Channel = channel;
        this.IconId = iconId;
        this.BaseValue = baseValue;
        this.Hits = 1;
        this.MergeId = mergeId;
        this.SkillName = skillName;
        this.RequiresDurationCheck = requiresDurCheck;
        this.StatusId = statusId;
        this.TargetObjectId = targetObjId;
        this.MaxDuration = maxDur;
        this.RemainingTime = remTime;
        this.DmgType = dmgType;
    }
}
