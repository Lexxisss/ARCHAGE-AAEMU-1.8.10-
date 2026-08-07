using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncClout : DoodadPhaseFuncTemplate
{
    private bool _missingShapeLogged;

    // doodad_phase_funcs
    public int Duration { get; set; }
    public int Tick { get; set; }
    public SkillTargetRelation TargetRelation { get; set; }
    public uint BuffId { get; set; }
    public uint ProjectileId { get; set; }
    public bool ShowToFriendlyOnly { get; set; }
    public int NextPhase { get; set; }
    public uint AoeShapeId { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
    public bool UseOriginSource { get; set; }
    public List<uint> Effects { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (caster is Character)
            Logger.Debug("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);
        else
            Logger.Trace("DoodadFuncClout : Duration {0}, Tick {1}, TargetRelationId {2}, BuffId {3}, ProjectileId {4}, ShowToFriendlyOnly {5}, NextPhase {6}, AoeShapeId {7}, TargetBuffTagId {8}, TargetNoBuffTagId {9}, UseOriginSource {10}", Duration, Tick, TargetRelation, BuffId, ProjectileId, ShowToFriendlyOnly, NextPhase, AoeShapeId, TargetBuffTagId, TargetNoBuffTagId, UseOriginSource);

        var areaTrigger = new AreaTrigger();
        areaTrigger.Shape = WorldManager.Instance.GetAreaShapeById(AoeShapeId);
        if (areaTrigger.Shape == null)
        {
            if (!_missingShapeLogged)
            {
                Logger.Warn("Skipping DoodadFuncClout {0}: AOE shape {1} was not found", Id, AoeShapeId);
                _missingShapeLogged = true;
            }

            return false;
        }

        if (UseOriginSource)
        {
            var doodads = WorldManager.GetAround<Doodad>(caster, areaTrigger.Shape.Value1, false);
            foreach (var d in doodads)
            {
                areaTrigger.Owner = d; // нам главное, чтобы рядом был doodad от которого будет искаться на кого наложить бафф
                break;
            }
            areaTrigger.Owner ??= owner;
        }
        else
        {
            areaTrigger.Owner = owner;
        }
        areaTrigger.Caster = caster as Unit;
        areaTrigger.AllowCasterlessTargets = areaTrigger.Caster == null &&
                                             TargetRelation is SkillTargetRelation.Any or SkillTargetRelation.Others;
        areaTrigger.InsideBuffTemplate = SkillManager.Instance.GetBuffTemplate(BuffId);
        areaTrigger.TargetRelation = TargetRelation;
        areaTrigger.TickRate = Tick;
        areaTrigger.EffectPerTick = Effects
            .Select(eid => SkillManager.Instance.GetEffectTemplate(eid))
            .Where(effect => effect != null)
            .ToList(); // SkillId = skillId

        if (BuffId > 0 && areaTrigger.InsideBuffTemplate == null)
        {
            Logger.Warn("Skipping DoodadFuncClout {0}: buff {1} was not found", Id, BuffId);
            return false;
        }

        Logger.Trace(
            "DoodadFuncClout active: func={0}, owner={1}/{2}, shape={3}, buff={4}, relation={5}, caster={6}, casterless={7}",
            Id,
            areaTrigger.Owner?.TemplateId ?? 0,
            areaTrigger.Owner?.ObjId ?? 0,
            AoeShapeId,
            BuffId,
            TargetRelation,
            areaTrigger.Caster?.ObjId ?? 0,
            areaTrigger.AllowCasterlessTargets);

        AreaTriggerManager.Instance.AddAreaTrigger(areaTrigger);

        if (Duration > 0)
        {
            // TODO : Add a proper delay in here
            // Schedule the task we just built; FuncTask can be cleared by a delete in between.
            var task = new DoodadFuncCloutTask(caster, owner, 0, NextPhase, areaTrigger);
            owner.FuncTask = task;
            TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(Duration));
        }
        //owner.OverridePhase = NextPhase; // Since phases trigger all at once let the doodad know its okay to stop here if the roll succeeded

        return false;
    }
}
