using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace MSBT;

internal sealed partial class Renderer
{
    private readonly Plugin plugin;
    private readonly DrawHelper drawHelper;
    private uint lastJobId = uint.MaxValue;
    private float passiveAuraTimer = 0f;

    private readonly HashSet<uint> failedIcons = new();
    private readonly Dictionary<uint, ISharedImmediateTexture> iconTextures = new();
    private readonly List<DisplayChannel> channelScratch = new(8);
    private readonly HashSet<uint> passiveStatusIds = new();
    private readonly Dictionary<uint, Dalamud.Game.ClientState.Objects.Types.IBattleChara?> statusTargetCache = new();
    private readonly Dictionary<DisplayChannel, List<CustomSCTNode>> channelNodes = new();

    public Renderer(Plugin plugin)
    {
        this.plugin = plugin;
        drawHelper = new DrawHelper(plugin.Configuration);
    }

    public void PlayInGameSound(int soundId)
    {
        if (soundId < 1 || soundId > 16) return;
        Service.Framework.RunOnFrameworkThread(() =>
        {
            try { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect((uint)soundId); } catch { }
        });
    }

    public string FormatNumber(int value, NumberFormatType formatType)
    {
        if (formatType == NumberFormatType.Space) return value.ToString("#,0").Replace(',', ' ');
        if (formatType == NumberFormatType.Comma) return value.ToString("#,0");
        if (formatType == NumberFormatType.Smart)
        {
            if (value >= 1000000) return (value / 1000000.0).ToString("0.##") + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#") + "k";
        }
        return value.ToString();
    }

    private int GetNodeLane(CustomSCTNode n, DisplayChannel ch)
    {
        bool treatAsCrit = n.IsCrit || (n.IsBigHit && ch.BigHitActsAsCrit);
        bool isCritStream = treatAsCrit && ch.CritBehavior != 0 && !n.IsAlert && !n.IsTextOnly;
        bool isStaticMode = ch.Direction == ScrollDirection.Static || ch.Direction == ScrollDirection.Pop || ch.Direction == ScrollDirection.Fade;

        if (isCritStream && ch.CritBehavior == 1) return 2;
        if (isCritStream && ch.CritBehavior == 2) return 3;

        return isStaticMode ? 1 : 0;
    }

    private int GetLaneFromParams(DisplayChannel ch, bool isCritStream)
    {
        bool isStaticMode = ch.Direction == ScrollDirection.Static || ch.Direction == ScrollDirection.Pop || ch.Direction == ScrollDirection.Fade;

        if (isCritStream && ch.CritBehavior == 1) return 2;
        if (isCritStream && ch.CritBehavior == 2) return 3;

        return isStaticMode ? 1 : 0;
    }

    internal float GetSpawnOffset(DisplayChannel ch, float scale, bool isCritStream)
    {
        if (ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay) return 0f;

        float spacing = isCritStream ? (75f * scale) : (45f * scale);
        int myLane = GetLaneFromParams(ch, isCritStream);

        for (int i = plugin.CustomTexts.Count - 1; i >= 0; i--)
        {
            CustomSCTNode node = plugin.CustomTexts[i];
            if (!node.IsActive || node.Channel != ch || GetNodeLane(node, ch) != myLane)
                continue;

            float lastTravel = node.TargetXOffset + (myLane == 0 ? node.Timer * ch.Speed : node.DistanceTraveled);
            if (lastTravel < spacing) return lastTravel - spacing;
            break;
        }

        return 0f;
    }

    private bool IsNodeVisible(CustomSCTNode node, DisplayChannel ch)
    {
        if (node.TargetObjectId == uint.MaxValue) return true;

        if (ch.CurrentTargetOnly)
        {
            var localPlayer = Service.ObjectTable.LocalPlayer;
            var currentTarget = Service.TargetManager.Target;

            bool isPlayer = localPlayer != null && node.TargetObjectId == localPlayer.EntityId;
            bool isCurrentTarget = currentTarget != null && node.TargetObjectId == currentTarget.EntityId;

            if (!isPlayer && !isCurrentTarget) return false;
        }
        return true;
    }

    private void SpawnPassiveNode(AuraTrigger trg, DisplayChannel ch, uint targetId, float remTime, float maxDur)
    {
        lock (plugin.TextNodesGate)
        {
            if (FindActiveStatusNode(ch, trg.StatusId, targetId) != null)
                return;

            var node = plugin.AcquireTextNode();
            string name = string.IsNullOrEmpty(trg.CustomText) ? plugin.Parser.GetSkillName(trg.StatusId) : trg.CustomText;
            uint icon = plugin.Parser.GetIconId(trg.StatusId);
            node.Init(name ?? "", name ?? "", 0, false, false, false, true, false, true, ch, icon, 0, trg.StatusId, name ?? "", false, trg.StatusId, targetId, maxDur, remTime, 0);
        }
    }

    private void SpawnStatusTrackerNode(DisplayChannel ch, uint statusId, uint targetId, float remTime, float maxDur)
    {
        lock (plugin.TextNodesGate)
        {
            if (FindActiveStatusNode(ch, statusId, targetId) != null)
                return;

            var node = plugin.AcquireTextNode();
            string name = plugin.Parser.GetSkillName(statusId);
            uint icon = plugin.Parser.GetIconId(statusId);
            node.Init(name ?? "", name ?? "", 0, false, false, false, true, false, false, ch, icon, 0, statusId, name ?? "", false, statusId, targetId, maxDur, remTime, 0);
        }
    }

    private CustomSCTNode? FindActiveStatusNode(DisplayChannel channel, uint statusId, uint targetId)
    {
        foreach (var node in plugin.CustomTexts)
        {
            if (node.IsActive && node.Channel == channel && node.StatusId == statusId && node.TargetObjectId == targetId)
                return node;
        }

        return null;
    }

    private void UpdatePassiveAuras()
    {
        var player = Service.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        var target = Service.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        if (player == null) return;
        foreach (var trg in plugin.Configuration.AuraTriggers)
        {
            if (!trg.Enabled || trg.StatusId == 0)
                continue;

            channelScratch.Clear();
            foreach (var channel in plugin.Configuration.Channels)
            {
                if (channel.Enabled &&
                    (channel.Mode == ChannelMode.Tracker || channel.Mode == ChannelMode.Overlay) &&
                    (trg.TargetChannels.Contains(channel.Name) || trg.TargetChannelName == channel.Name))
                    channelScratch.Add(channel);
            }

            if (channelScratch.Count == 0)
                continue;

            bool foundOnPlayer = false; float pRemTime = 0f; float pMaxDur = 0f;
            foreach (var status in player.StatusList)
            {
                if (status.StatusId == trg.StatusId)
                {
                    if (trg.OnlyCastByMe && status.SourceId != player.EntityId) continue;
                    foundOnPlayer = true; pRemTime = status.RemainingTime; pMaxDur = status.RemainingTime;
                    break;
                }
            }
            if (foundOnPlayer && plugin.Parser.CheckConditions(trg.Conditions, player, target))
                foreach (var ch in channelScratch) SpawnPassiveNode(trg, ch, player.EntityId, pRemTime, pMaxDur);

            if (target != null)
            {
                bool foundOnTarget = false; float tRemTime = 0f; float tMaxDur = 0f;
                foreach (var status in target.StatusList)
                {
                    if (status.StatusId == trg.StatusId)
                    {
                        if (trg.OnlyCastByMe && status.SourceId != player.EntityId) continue;
                        foundOnTarget = true; tRemTime = status.RemainingTime; tMaxDur = status.RemainingTime;
                        break;
                    }
                }
                if (foundOnTarget && plugin.Parser.CheckConditions(trg.Conditions, player, target))
                    foreach (var ch in channelScratch) SpawnPassiveNode(trg, ch, target.EntityId, tRemTime, tMaxDur);
            }
        }
    }

    private void UpdateGenericTrackers()
    {
        var player = Service.ObjectTable.LocalPlayer as Dalamud.Game.ClientState.Objects.Types.IBattleChara;
        var target = Service.TargetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleChara;

        if (player == null) return;

        channelScratch.Clear();
        foreach (var channel in plugin.Configuration.Channels)
        {
            if (channel.Enabled && channel.Mode == ChannelMode.Tracker)
                channelScratch.Add(channel);
        }
        if (channelScratch.Count == 0) return;

        passiveStatusIds.Clear();
        foreach (var trigger in plugin.Configuration.AuraTriggers)
        {
            if (trigger.Enabled && trigger.StatusId > 0)
                passiveStatusIds.Add(trigger.StatusId);
        }

        if (target != null && target.EntityId != player.EntityId)
        {
            foreach (var status in target.StatusList)
            {
                if (passiveStatusIds.Contains(status.StatusId)) continue;

                bool isFromMe = status.SourceId == player.EntityId;
                if (isFromMe)
                {
                    foreach (var ch in channelScratch)
                    {
                        if (ch.AcceptsOutgoingStatuses)
                        {
                            SpawnStatusTrackerNode(ch, status.StatusId, target.EntityId, status.RemainingTime, status.RemainingTime);
                        }
                    }
                }
            }
        }
    }

    private IDalamudTextureWrap? GetIconWrap(uint iconId)
    {
        if (iconId == 0 || iconId == 405 || failedIcons.Contains(iconId)) return null;
        try
        {
            if (!iconTextures.TryGetValue(iconId, out ISharedImmediateTexture? texture))
            {
                texture = Service.TextureProvider.GetFromGameIcon(new GameIconLookup { IconId = iconId });
                iconTextures[iconId] = texture;
            }

            return texture.GetWrapOrDefault();
        }
        catch
        {
            failedIcons.Add(iconId);
            return null;
        }
    }

    private void BuildChannelNodeBuckets()
    {
        foreach (List<CustomSCTNode> nodes in channelNodes.Values)
            nodes.Clear();

        foreach (CustomSCTNode node in plugin.CustomTexts)
        {
            if (!channelNodes.TryGetValue(node.Channel, out List<CustomSCTNode>? nodes))
            {
                nodes = new List<CustomSCTNode>(16);
                channelNodes[node.Channel] = nodes;
            }
            nodes.Add(node);
        }
    }

    public void SpawnTestText(bool isCrit, DisplayChannel ch, bool isHeal = false, bool isAlert = false)
    {
        bool isDirectHit = isCrit;
        string marks = (isCrit && isDirectHit) ? "!!" : (isCrit ? "!" : (isDirectHit ? "*" : ""));
        string txt = ch.IconOnRight ? (marks + "9999") : ("9999" + marks);
        string name = "Test Test Test";

        if (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay)
        {
            txt = "Test Status";
            if (ch.ShowStatusPrefixes && !ch.HideSkillNames) txt = ch.IconOnRight ? txt + " +" : "+ " + txt;
            if (ch.Mode != ChannelMode.Tracker && ch.ShowStatusDuration && !ch.HideSkillNames) txt = ch.IconOnRight ? "(15s) " + txt : txt + " (15s)";
        }
        else if (!ch.HideSkillNames) { txt = ch.IconOnRight ? $"{txt} {name}" : $"{name} {txt}"; }

        uint fakeIcon = 15004; if (ch.HideIcons && ch.TrackerStyle == TrackerStyle.Text) fakeIcon = 0;
        if (isCrit && ch.CritSound > 0) PlayInGameSound(ch.CritSound);
        if (isAlert && ch.AlertSound > 0) PlayInGameSound(ch.AlertSound);

        lock (plugin.TextNodesGate)
        {
            int critBehavior = (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay) ? 0 : ch.CritBehavior;
            bool treatAsCrit = isCrit || ch.BigHitActsAsCrit;
            bool isCritStream = treatAsCrit && critBehavior != 0 && !isAlert && ch.Mode == ChannelMode.Scrolling;

            float spawnOffset = GetSpawnOffset(ch, isCritStream ? ch.CritScale : ch.NormalScale, isCritStream);

            var node = plugin.AcquireTextNode();

            node.Init(txt, txt, spawnOffset, isCrit, isDirectHit, isHeal, (isAlert || ch.Mode == ChannelMode.Tracker || ch.Mode == ChannelMode.Overlay), false, isAlert, ch, fakeIcon, 9999, uint.MaxValue, name, false, 0, 0, 10f, 10f, 1);
            node.DistanceTraveled = 0f;
        }
    }

    public void SpawnIpcAlert(string text, DisplayChannel ch, int soundId)
    {
        if (soundId > 0) PlayInGameSound(soundId);
        else if (ch.AlertSound > 0) PlayInGameSound(ch.AlertSound);

        lock (plugin.TextNodesGate)
        {
            float spawnOffset = GetSpawnOffset(ch, ch.NormalScale, false);

            var node = plugin.AcquireTextNode();

            node.Init(text ?? "", text ?? "", spawnOffset, false, false, false, true, false, true, ch, 0, 0, uint.MaxValue, "", false, 0, 0, 10f, 10f, 0);
            node.DistanceTraveled = 0f;
        }
    }
}
