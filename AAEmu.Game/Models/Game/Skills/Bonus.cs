using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class Bonus
{
    private int _value;

    public BonusTemplate Template { get; set; }
    public DynamicBonusTemplate DynamicTemplate { get; set; }
    public Unit Owner { get; set; }
    public Buff SourceBuff { get; set; }

    public int Value
    {
        get => DynamicTemplate?.Evaluate(Owner, SourceBuff) ?? _value;
        set => _value = value;
    }
}
