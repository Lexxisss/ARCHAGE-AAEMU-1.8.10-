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

/// <summary>
/// Server-authoritative straight dash to the plot target position.
/// Target-client animation remains driven by SCPlotEventPacket.
/// </summary>
public sealed class DashSkillController : SkillController
{
    private readonly int _durationMs;
    private Vector3 _start;
    private Vector3 _end;
    private DateTime _startedAt;

    public DashSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target;
        _durationMs = Math.Clamp(template.Value[0] > 0 ? template.Value[0] : 300, 50, 10000);
    }

    public override void Execute()
    {
        if (Owner == null || Target?.Transform == null)
        {
            End();
            return;
        }

        _start = Owner.Transform.World.Position;
        _end = Target.Transform.World.Position;
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
        if (Owner == null || Owner.IsDead || Owner.Buffs.HasEffectsMatchingCondition(effect =>
                effect.Template.Stun || effect.Template.Sleep || effect.Template.Root || effect.Template.Knockdown))
        {
            End();
            return;
        }

        var elapsed = (DateTime.UtcNow - _startedAt).TotalMilliseconds;
        var progress = (float)Math.Clamp(elapsed / _durationMs, 0d, 1d);
        var next = Vector3.Lerp(_start, _end, progress);
        MoveAndBroadcast(next);

        if (progress >= 1f)
            End();
    }

    private void MoveAndBroadcast(Vector3 position)
    {
        var oldPosition = Owner.Transform.Local.ClonePosition();
        var z = position.Z;
        var height = WorldManager.Instance.GetHeight(Owner.Transform.ZoneId, position.X, position.Y);
        if (height != 0 && Math.Abs(z - height) < 3f)
            z = height;

        Owner.Transform.Local.SetPosition(position.X, position.Y, z);
        var angle = MathUtil.CalculateAngleFrom(_start, _end);
        Owner.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90f);
        var (rx, ry, rz) = Owner.Transform.Local.ToRollPitchYawSBytesMovement();
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());

        var move = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        move.X = Owner.Transform.Local.Position.X;
        move.Y = Owner.Transform.Local.Position.Y;
        move.Z = Owner.Transform.Local.Position.Z;
        move.VelX = (short)velX;
        move.VelY = (short)velY;
        move.RotationX = rx;
        move.RotationY = ry;
        move.RotationZ = rz;
        move.ActorFlags = 0;
        move.Flags = 4;
        move.DeltaMovement = new sbyte[3] { 0, 127, 0 };
        move.Stance = 0;
        move.Alertness = 2;
        move.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Owner.CheckMovedPosition(oldPosition);
        Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, move), false);
    }
}
