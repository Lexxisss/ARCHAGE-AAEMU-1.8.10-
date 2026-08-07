using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelEndTimerTask : Task
{
    protected Duel _duel;
    protected uint _challengerId;

    public DuelEndTimerTask(Duel duel, uint challengerId)
    {
        _duel = duel;
        _challengerId = challengerId;
    }

    public override async void Execute()
    {
        // Read the field once: DuelManager ends duels from other threads and clears this very
        // field, so testing it and dereferencing it separately is a crash waiting for the timing.
        var endTimerTask = _duel.DuelEndTimerTask;
        if (endTimerTask == null)
            return;

        _duel.DuelEndTimerTask = null;
        await endTimerTask.CancelAsync();

        if (_duel.Challenger.Hp < _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_duel.Challenged.Id, DuelDetType.Win, _challengerId);
        }
        else if (_duel.Challenger.Hp > _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Win, _duel.Challenged.Id);
        }
        else if (_duel.Challenger.Hp == _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Draw, _duel.Challenged.Id);
        }
    }
}
