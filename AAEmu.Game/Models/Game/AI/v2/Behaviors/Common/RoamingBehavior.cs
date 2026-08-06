using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RoamingBehavior : BaseCombatBehavior
{
    private Vector3 _targetRoamPosition = Vector3.Zero;
    private DateTime _nextRoaming;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        // Do not resend ActorModelState with activate=false when roaming starts.
        // The initial posture is already carried by SCUnitState; resending it here
        // resets the animation state exactly when locomotion is about to begin.
        //UpdateRoaming();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        CheckAggression();

        if (_targetRoamPosition.Equals(Vector3.Zero) && DateTime.UtcNow > _nextRoaming)
            UpdateRoaming();

        if (_targetRoamPosition.Equals(Vector3.Zero))
            return;

        Ai.Owner.MoveTowards(_targetRoamPosition, Ai.Owner.BaseMoveSpeed * (delta.Milliseconds / 1000.0f));
        var dist = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, _targetRoamPosition);
        if (dist < 1.0f)
        {
            Ai.Owner.StopMovement();
            _targetRoamPosition = Vector3.Zero;
            _nextRoaming = DateTime.UtcNow.AddSeconds(Rand.Next(3, 6)); // Rand 3-6 would look nice ?
        }
    }

    public override void Exit()
    {
        _enter = false;
    }

    private void UpdateRoaming()
    {
        // TODO : Group member handling
        using (var transform = AIUtils.CalcNextRoamingPosition(Ai))
        {
            _targetRoamPosition = transform.Local.Position;
        }
    }
}
