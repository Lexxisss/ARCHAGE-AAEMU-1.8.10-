using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotNode
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Tree
    public PlotTree Tree;
    public PlotNode Parent;
    public List<PlotNode> Children;
    // Plots
    public PlotEventTemplate Event;
    public PlotNextEvent ParentNextEvent;

    public PlotNode()
    {
        Children = [];
    }

    private bool IsChannelStart()
    {
        return Children.Any(child => child.ParentNextEvent?.Channeling ?? false);
    }

    public int ComputeDelayMs(PlotState state, PlotTargetInfo targetInfo)
    {
        return ParentNextEvent?.GetDelay(state, targetInfo, Parent) ?? 0;
    }

    public bool CheckConditions(PlotState state, PlotTargetInfo targetInfo)
    {
        return Event?.Conditions?.All(condition => condition.CheckCondition(state, targetInfo)) ?? true;
    }

    public void Execute(PlotState state, PlotTargetInfo targetInfo, CompressedGamePackets packets = null)
    {
        //Logger.Debug("Executing plot node with id {0}", Event.Id);

        if (state?.Caster == null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        byte flag = 2;

        foreach (var eff in Event.Effects)
        {
            try
            {
                eff.ApplyEffect(state, targetInfo, Event, ref flag, IsChannelStart(), packets);
            }
            catch (Exception e)
            {
                state.Caster?.SendPacket(new SCChatMessagePacket(Chat.ChatType.Notice, "Plot Effects Error - Check Logs"));
                Logger.Error("[Plot Effects Error]: {0}\n{1}", e.Message, e.StackTrace);
            }
        }

        double castTime = Event.NextEvents
            .Where(nextEvent => nextEvent.Casting || nextEvent.Channeling)
            .Max(nextEvent => nextEvent.Delay / 10 as int?) ?? 0;

        castTime = state.Caster.ApplySkillModifiers(state.ActiveSkill, SkillAttribute.CastTime, castTime) * state.Caster.CastTimeMul;
        castTime = Math.Max(castTime, 0);

        if (castTime > 0)
            state.IsCasting = true;

        if (ParentNextEvent?.Casting ?? false)
            state.IsCasting = false;

        if (ParentNextEvent?.Channeling ?? false)
            state.IsChanneling = false;
        else
            state.IsChanneling = true;

        if (Event == null || (!Event.HasSpecialEffects() && !(castTime > 0) && Event.Conditions.Count <= 0))
        {
            return;
        }

        var skill = state.ActiveSkill;
        var unkId = (ParentNextEvent?.Casting ?? false) || (ParentNextEvent?.Channeling ?? false)
            ? state.Caster.ObjId
            : 0;

        var packetSource = targetInfo.Source ?? state.Caster;
        var packetTarget = targetInfo.Target ?? state.Target ?? packetSource;
        if (packetSource?.Transform == null || packetTarget?.Transform == null)
        {
            Logger.Warn("Plot event packet skipped due to missing source/target transform: skill={0}, plot={1}, event={2}",
                skill.Template.Id, Event.PlotId, Event.Id);
            return;
        }

        var casterPlotObj = packetSource.ObjId == uint.MaxValue
            ? new PlotObject(packetSource.Transform, packetSource.Transform)
            : new PlotObject(packetSource);

        // A POSITION target carries both the endpoint and the line origin. The latter is the
        // packet source/caster transform; omitting it made Nitro and both somersault controllers
        // land on the same shifted SCPlotEvent layout client-side.
        var targetPlotObj = packetTarget.ObjId == uint.MaxValue
            ? new PlotObject(packetTarget.Transform, packetSource.Transform)
            : new PlotObject(packetTarget);

        if (packetTarget.ObjId == uint.MaxValue)
        {
            var endpoint = packetTarget.Transform.World.Position;
            var lineOrigin = packetSource.Transform.World.Position;
            Logger.Debug(
                "Plot POSITION: skill={0}, event={1}, endpoint=({2:F2},{3:F2},{4:F2}), line=({5:F2},{6:F2},{7:F2})",
                skill.Template.Id,
                Event.Id,
                endpoint.X,
                endpoint.Y,
                endpoint.Z,
                lineOrigin.X,
                lineOrigin.Y,
                lineOrigin.Z);
        }

        // targetUnitCount is a list of real unit object ids. Location pseudo-targets
        // use uint.MaxValue and must not be serialized as a fake BC target.
        var targetUnitIds = targetInfo.EffectedTargets
            .Where(target => target != null && target.ObjId != 0 && target.ObjId != uint.MaxValue)
            .Select(target => target.ObjId)
            .Distinct()
            .ToArray();

        var packet = new SCPlotEventPacket(skill.TlId, Event.Id, skill.Template.Id, casterPlotObj,
            targetPlotObj, unkId, (ushort)castTime, flag, 0, targetUnitIds,
            state.SkillObject?.InputDirection ?? 0);

        if (packets != null)
            packets.AddPacket(packet);
        else
            state.Caster.BroadcastPacket(packet, true);

        Logger.Trace($"Execute Took {stopwatch.ElapsedMilliseconds} to finish.");
    }
}
