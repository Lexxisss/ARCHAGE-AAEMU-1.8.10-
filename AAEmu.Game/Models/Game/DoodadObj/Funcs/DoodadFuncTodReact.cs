using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Moves a doodad to another phase while the clock stands inside a given window.
/// </summary>
/// <remarks>
/// The sibling DoodadFuncTod compares against a single moment; this one carries both ends, so it
/// describes a stretch of the day rather than a threshold. Times are hhmm, and is_realtime picks
/// the server's own clock over the game world's.
/// </remarks>
public class DoodadFuncTodReact : DoodadPhaseFuncTemplate
{
    public int Tod { get; set; }
    public int TodEnd { get; set; }
    public int NextPhase { get; set; }
    public bool IsRealtime { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (NextPhase <= 0)
        {
            return false;
        }

        var now = IsRealtime
            ? (float)System.DateTime.UtcNow.TimeOfDay.TotalHours
            : TimeManager.Instance.GetTime();

        var from = Tod / 100f;
        var to = TodEnd / 100f;

        // A window may run through midnight, in which case its end reads as smaller than its start.
        var inside = from <= to
            ? now >= from && now < to
            : now >= from || now < to;

        Logger.Trace("DoodadFuncTodReact {0:0.00}..{1:0.00} (realtime {2}), now {3:0.00}, inside {4}",
            from, to, IsRealtime, now, inside);

        if (!inside)
        {
            return false;
        }

        owner.OverridePhase = NextPhase;
        return true;
    }
}
