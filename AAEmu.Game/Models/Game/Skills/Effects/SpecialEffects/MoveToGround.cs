using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class MoveToGround : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target,
        SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject,
        DateTime time, int value1, int value2, int value3, int value4)
    {
        var unit = target as Unit ?? caster as Unit;
        if (unit == null)
            return;

        var ground = WorldManager.Instance.GetHeight(unit.Transform.ZoneId,
            unit.Transform.World.Position.X,
            unit.Transform.World.Position.Y);
        if (ground == 0)
            return;

        var oldPosition = unit.Transform.Local.ClonePosition();
        unit.Transform.Local.SetHeight(ground + Math.Max(0, value1) / 1000f);
        var move = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        move.X = unit.Transform.Local.Position.X;
        move.Y = unit.Transform.Local.Position.Y;
        move.Z = unit.Transform.Local.Position.Z;
        move.ActorFlags = 0;
        move.Flags = 4;
        move.DeltaMovement = new sbyte[3] { 0, 0, 0 };
        move.Stance = 0;
        move.Alertness = 0;
        move.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        unit.CheckMovedPosition(oldPosition);
        unit.BroadcastPacket(new SCOneUnitMovementPacket(unit.ObjId, move), false);
    }
}
