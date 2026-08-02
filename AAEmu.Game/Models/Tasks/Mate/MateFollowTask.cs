using System;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Tasks.Mate;

/// <summary>
/// Walks a mate after its owner while nobody is riding it.
/// </summary>
/// <remarks>
/// Mates had no movement of their own at all: they were put down where they were summoned and
/// stayed there, because following lives on the NPC brain and a mate is a plain unit. This is
/// deliberately not that brain - no aggression, no paths, no combat - just the one behaviour a
/// mate needs, so nothing that already works for NPCs is disturbed.
///
/// The pace matches what the client accepts from its own movement: one state per tick with a
/// speed that agrees with the ground covered, and one final state carrying no speed when the
/// animal arrives, or it drifts on past where the server has it.
/// </remarks>
public class MateFollowTask : Task
{
    /// <summary>Milliseconds between steps. Also the divisor that turns a step into a speed.</summary>
    public const int TickIntervalMs = Game.Units.Mate.FollowTickIntervalMs;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Game.Units.Mate _mate;

    public MateFollowTask(Game.Units.Mate mate)
    {
        _mate = mate;
    }

    public override void Execute()
    {
        var mate = _mate;
        if (mate == null || mate.ObjId == 0 || mate.IsDead)
        {
            Stop();
            return;
        }

        // A rider steers; the server has nothing to add while somebody is in the saddle.
        if (mate.IsRidden)
        {
            mate.StopMovement();
            return;
        }

        var owner = WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId);
        if (owner == null || owner.IsDead || owner.Transform.WorldId != mate.Transform.WorldId)
        {
            mate.StopMovement();
            return;
        }

        var ownerPosition = owner.Transform.World.Position;
        var matePosition = mate.Transform.World.Position;
        var distance = MathUtil.CalculateDistance(matePosition, ownerPosition, true);

        if (distance <= FollowDistance)
        {
            mate.StopMovement();
            return;
        }

        // Left too far behind - a doorway, a lift, a teleport - walking back is hopeless, so put
        // it beside its owner and announce that as an ordinary state.
        if (distance >= TeleportDistance)
        {
            Logger.Debug($"MateFollow: mate {mate.ObjId} was {distance:F1} behind owner {owner.ObjId}, moved to them");
            mate.Transform.Local.SetPosition(ownerPosition.X, ownerPosition.Y, ownerPosition.Z);
            mate.StopMovement();
            mate.MoveTowards(ownerPosition, 0.02f);
            return;
        }

        // The further behind it is the harder it runs, up to a limit, so it closes a gap instead
        // of trailing at exactly the owner's pace forever.
        var speedMultiplier = Math.Min(MaxSpeedMultiplier, distance / FollowDistance);
        var speedPerSecond = Math.Max(mate.BaseMoveSpeed, 1f) * mate.MoveSpeedMul * speedMultiplier;
        var step = speedPerSecond * (TickIntervalMs / 1000f);

        // Aim for the near side of the owner rather than their feet, or the mate ends up standing
        // inside them and jitters between the two checks above.
        var direction = Vector3.Normalize(new Vector3(
            matePosition.X - ownerPosition.X,
            matePosition.Y - ownerPosition.Y,
            0f));
        var target = float.IsNaN(direction.X)
            ? ownerPosition
            : ownerPosition + direction * FollowDistance;

        mate.MoveTowards(target, step);
    }

    private void Stop()
    {
        _ = _mate?.MateFollowTask?.CancelAsync();
        if (_mate != null)
            _mate.MateFollowTask = null;
    }

    /// <summary>How close the mate settles before it stops walking.</summary>
    private const float FollowDistance = 3f;

    /// <summary>Past this it is put beside its owner instead of walking.</summary>
    private const float TeleportDistance = 60f;

    /// <summary>The most a trailing mate may exceed its own speed by.</summary>
    private const float MaxSpeedMultiplier = 5f;
}
