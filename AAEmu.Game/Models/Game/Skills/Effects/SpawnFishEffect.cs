using System;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnFishEffect : EffectTemplate
{
    public uint Range { get; set; }
    public uint DoodadId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character character)
            return;

        var searchPosition = character.Transform.World.Position;
        switch (targetObj)
        {
            case SkillCastPositionTarget position:
                searchPosition = new Vector3(position.PosX, position.PosY, position.PosZ);
                break;
            case SkillCastPosition2Target position:
                searchPosition = new Vector3(position.PosX, position.PosY, position.PosZ);
                break;
            case SkillCastPosition3Target position:
                searchPosition = new Vector3(position.PosX, position.PosY, position.PosZ);
                break;
            default:
                if (target != null)
                    searchPosition = target.Transform.World.Position;
                break;
        }

        var radius = Range / 1000f;
        var school = FishSchoolManager.Instance.GetAllFishSchools()
            .Where(doodad =>
                doodad.Transform.WorldId == character.Transform.WorldId &&
                (DoodadId == 0 || doodad.TemplateId == DoodadId) &&
                MathUtil.CalculateDistance(searchPosition, doodad.Transform.World.Position, true) <= radius)
            .OrderBy(doodad => MathUtil.CalculateDistance(searchPosition, doodad.Transform.World.Position, true))
            .FirstOrDefault();

        if (school == null)
        {
            Logger.Debug("SpawnFishEffect: no fish school in range={0}m for character={1}", radius, character.Name);
            return;
        }

        var phaseFunc = DoodadManager.Instance.GetDoodadPhaseFuncs(school.FuncGroupId)
            .FirstOrDefault(func => func.FuncType == nameof(DoodadFuncFishSchool));
        if (phaseFunc == null)
        {
            Logger.Warn("SpawnFishEffect: doodad objId={0} template={1} has no fish-school function in current phase={2}",
                school.ObjId, school.TemplateId, school.FuncGroupId);
            return;
        }

        if (DoodadManager.Instance.GetPhaseFuncTemplate(phaseFunc.FuncId, phaseFunc.FuncType) is not DoodadFuncFishSchool fishSchool)
        {
            Logger.Warn("SpawnFishEffect: missing function template id={0}", phaseFunc.FuncId);
            return;
        }

        fishSchool.Use(character, school);
    }
}
