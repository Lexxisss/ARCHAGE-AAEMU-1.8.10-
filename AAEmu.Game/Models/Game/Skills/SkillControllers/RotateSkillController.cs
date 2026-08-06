using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public sealed class RotateSkillController : SkillController
{
    private readonly float _rotationDegrees;
    private readonly int _durationMs;
    private float _startYaw;
    private float _targetYaw;
    private DateTime _startedAt;

    public RotateSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target;
        _rotationDegrees = template.Value[0];
        _durationMs = Math.Clamp(template.Value[2] > 0 ? template.Value[2] : 100, 1, 30000);
    }

    public override void Execute()
    {
        if (Owner == null)
        {
            End();
            return;
        }

        _startYaw = Owner.Transform.Local.Rotation.Z.RadToDeg();
        _targetYaw = Target?.Transform != null && Target.ObjId != Owner.ObjId
            ? (float)MathUtil.CalculateAngleFrom(Owner.Transform.World.Position, Target.Transform.World.Position) - 90f + _rotationDegrees
            : _startYaw + _rotationDegrees;
        _startedAt = DateTime.UtcNow;
        base.Execute();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(25));
    }

    public override void End()
    {
        if (State == SCState.Ended)
            return;

        TickManager.Instance.OnTick.UnSubscribe(Tick);
        base.End();
    }

    private void Tick(TimeSpan delta)
    {
        if (Owner == null || Owner.IsDead)
        {
            End();
            return;
        }

        var progress = (float)Math.Clamp((DateTime.UtcNow - _startedAt).TotalMilliseconds / _durationMs, 0d, 1d);
        var yaw = _startYaw + (_targetYaw - _startYaw) * progress;
        Owner.Transform.Local.SetRotationDegree(0f, 0f, yaw);
        BroadcastRotation();
        if (progress >= 1f)
            End();
    }

    private void BroadcastRotation()
    {
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();
        var move = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        move.X = Owner.Transform.Local.Position.X;
        move.Y = Owner.Transform.Local.Position.Y;
        move.Z = Owner.Transform.Local.Position.Z;
        move.RotationX = rx;
        move.RotationY = ry;
        move.RotationZ = rz;
        move.ActorFlags = 0;
        move.Flags = 4;
        move.DeltaMovement = new sbyte[3] { 0, 0, 0 };
        move.Stance = 0;
        move.Alertness = 2;
        move.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, move), false);
    }
}
