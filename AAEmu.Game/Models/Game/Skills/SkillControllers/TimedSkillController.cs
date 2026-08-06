using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Lifecycle controller for client-driven controller kinds (rope, anchor, flowgraph,
/// impulse, move and crawl). It keeps the server-side skill state finite instead of
/// dropping the controller or leaving the unit permanently blocked.
/// </summary>
public class TimedSkillController : SkillController
{
    private readonly int _durationMs;
    private DateTime _endAt;

    public TimedSkillController(SkillControllerTemplate template, BaseUnit owner, BaseUnit target, int durationMs)
    {
        Template = template;
        Owner = owner as Unit;
        Target = target;
        _durationMs = Math.Clamp(durationMs, 1, 60000);
    }

    public override void Execute()
    {
        if (Owner == null)
        {
            End();
            return;
        }

        base.Execute();
        _endAt = DateTime.UtcNow.AddMilliseconds(_durationMs);
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(50));
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
        if (Owner == null || Owner.IsDead || DateTime.UtcNow >= _endAt)
            End();
    }
}
