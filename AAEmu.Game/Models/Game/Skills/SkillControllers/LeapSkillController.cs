using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public class LeapSkillController : SkillController
{
    public int Angle { get; set; }
    public int Speed { get; set; }
    public int Duration { get; set; }
    public int DistanceOffset { get; set; }

    private float _calculatedSpeed;
    private Vector3 _endPosition;
    public enum LeapDirection
    {
        Both = 0,
        ForwardOnly = 1,
        BackwardOnly = 2
    }
    public LeapDirection Direction { get; set; }

    public LeapSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target;

        if (Owner == null || Target?.Transform == null)
            return;

        Angle = template.Value[0];
        Speed = template.Value[1];
        Duration = Math.Max(template.Value[2], 1);
        DistanceOffset = template.Value[3];
        Direction = (LeapDirection)template.Value[6];

        var angle = (float)MathUtil.CalculateAngleFrom(owner, target);
        (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(DistanceOffset / 1000f, target.Transform.World.Position.X, target.Transform.World.Position.Y, angle);
        _endPosition.Z = Target.Transform.World.Position.Z;

        var distance = MathUtil.CalculateDistance(Owner.Transform.World.Position, _endPosition, true);
        _calculatedSpeed = distance / (Duration / 1000f);

    }

    public void Tick(TimeSpan delta)
    {
        if (Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep) || Owner.IsDead)
        {
            End();
            return;
        };
        var elapsedSeconds = (float)(delta.TotalMilliseconds / 1000f);
        MoveTowards(_calculatedSpeed * elapsedSeconds, elapsedSeconds);
    }

    public override void Execute()
    {
        if (Owner == null || Target?.Transform == null)
        {
            End();
            return;
        }

        base.Execute();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(50));
    }

    public override void End()
    {
        if (State == SCState.Ended)
            return;

        TickManager.Instance.OnTick.UnSubscribe(Tick);
        base.End();
    }

    public void MoveTowards(float distance, float elapsedSeconds)
    {
        distance *= Owner.MoveSpeedMul;
        if (distance < 0.01f || elapsedSeconds <= 0f)
        {
            End();
            return;
        }

        if (Owner.Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Owner.IsDead)
        {
            End();
            return;
        }

        if (Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle))
            || Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            End();
            return;
        }

        var oldPosition = Owner.Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Owner.Transform.Local.Position, _endPosition, true);
        var completed = targetDist <= distance || targetDist <= 0.05f;
        var travelDist = completed ? targetDist : distance;

        if (completed)
        {
            Owner.Transform.Local.SetPosition(_endPosition.X, _endPosition.Y, _endPosition.Z);
        }
        else
        {
            var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
                travelDist, targetDist, Owner.Transform.Local.Position, _endPosition);
            Owner.Transform.Local.SetPosition(newX, newY, newZ);

            // Plot area targets (ObjId == uint.MaxValue) already carry the height offset
            // encoded by the glider/leap event. Ground-snapping them erases lift and rolls.
            var preservesPlotHeight = Target.ObjId == uint.MaxValue
                                      || Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Gliding);
            if (!preservesPlotHeight)
            {
                var updZ = WorldManager.Instance.GetHeight(Owner.Transform.ZoneId, newX, newY);
                if (Math.Abs(newZ - updZ) < 1f)
                    Owner.Transform.Local.SetHeight(updZ);
            }
        }

        var angle = MathUtil.CalculateAngleFrom(oldPosition, _endPosition);
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();
        var displacement = Owner.Transform.Local.Position - oldPosition;

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = Owner.Transform.Local.Position.X;
        moveType.Y = Owner.Transform.Local.Position.Y;
        moveType.Z = Owner.Transform.Local.Position.Z;
        moveType.VelX = completed
            ? (short)0
            : (short)Math.Clamp(displacement.X / elapsedSeconds / 60f * 32768f, short.MinValue, short.MaxValue);
        moveType.VelY = completed
            ? (short)0
            : (short)Math.Clamp(displacement.Y / elapsedSeconds / 60f * 32768f, short.MinValue, short.MaxValue);
        moveType.VelZ = completed
            ? (short)0
            : (short)Math.Clamp(displacement.Z / elapsedSeconds / 60f * 32768f, short.MinValue, short.MaxValue);
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = 0;
        // As in NPC movement, common Flags bit 0x04 suppresses the client branch
        // that consumes stance/locomotion state (target 0x391FF970).
        moveType.Flags = 0;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[1] = completed ? (sbyte)0 : (sbyte)127;
        moveType.Stance = 0;
        moveType.Alertness = 0;
        moveType.Time = unchecked((uint)Environment.TickCount64);

        Owner.CheckMovedPosition(oldPosition);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);

        if (completed)
            End();
    }
}
