using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Puts neighbouring doodads of one template into a given phase, then carries on itself.
/// </summary>
/// <remarks>
/// This is how a doodad speaks to its companions - the Solzreed tribute post, for one, is what
/// opens the ship-scroll vendor standing beside it. Reach is the surrounding regions, the same
/// scope DoodadFuncPulse already uses to find its related doodads; a template id alone would
/// otherwise mean every copy in the world, and these come in one set per region.
/// </remarks>
public class DoodadFuncChangeOtherDoodadPhase : DoodadPhaseFuncTemplate
{
    public uint TargetDoodadId { get; set; }
    public int TargetPhase { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncChangeOtherDoodadPhase target {0} to phase {1}, then {2}",
            TargetDoodadId, TargetPhase, NextPhase);

        if (TargetDoodadId > 0 && TargetPhase > 0 && owner != null)
        {
            var targets = WorldManager.GetAround<Doodad>(owner)
                .Where(doodad => doodad.TemplateId == TargetDoodadId && doodad.FuncGroupId != TargetPhase)
                .ToList();

            foreach (var target in targets)
            {
                Logger.Debug("DoodadFuncChangeOtherDoodadPhase: doodad {0} moves {1} ({2}) to phase {3}",
                    owner.TemplateId, target.ObjId, target.TemplateId, TargetPhase);
                target.DoChangePhase(caster, TargetPhase);
            }
        }

        if (NextPhase <= 0)
        {
            return false; // nowhere of its own to go; let the remaining phase functions run
        }

        owner.OverridePhase = NextPhase;
        return true;
    }
}
