using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// Per-tick mana drain for a buff that charges mana while it is up, such as Sprint.
/// The cost is not stored on the skill - skill 16287 has no mana cost at all in the
/// database. It comes from the buff's tick_level_mana_cost (0.5 on buff 2675, ticking
/// every 200 ms), scaled by the level mana formula.
/// </summary>
public class ManaRegenTemplate
{
    public Character Owner { get; }

    /// <summary>Buff tick interval in milliseconds.</summary>
    private double Tick { get; }

    /// <summary>Mana cost multiplier applied to the level mana formula, per tick.</summary>
    private double TickLevelManaCost { get; }

    private int Level { get; }

    public ManaRegenTemplate(Character owner, double tick, double tickLevelManaCost, int level)
    {
        Owner = owner;
        Tick = tick;
        TickLevelManaCost = tickLevelManaCost;
        Level = level;
    }

    private double CalculateManaCostPerTick()
    {
        // Dash's ability level tracks the character level, so the level mana formula is
        // evaluated at the character's level and scaled by the buff's per-tick multiplier.
        var manaPerTickFormula = FormulaManager.Instance.GetFormula((uint)UnitFormulaKind.LevelMana);
        if (manaPerTickFormula == null)
            return 0;

        var parameters = new Dictionary<string, double> { { "ab_level", Level } };
        return manaPerTickFormula.Evaluate(parameters) * TickLevelManaCost;
    }

    /// <summary>
    /// Charges one tick of mana. Returns false when the owner can no longer pay, which is
    /// the signal to drop the buff.
    /// </summary>
    public bool ConsumeTick()
    {
        if (Owner == null || !Owner.Buffs.CheckBuff((uint)BuffConstants.Dash))
            return false;

        var manaPerTick = CalculateManaCostPerTick();
        if (manaPerTick <= 0)
            return true; // Nothing to charge, let the buff run.

        if (Owner.Mp < manaPerTick)
            return false;

        Owner.ReduceCurrentMp(null, (int)manaPerTick);
        return true;
    }
}
