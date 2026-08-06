using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class IncomingSiegeDamageMultiplierTests
{
    [Fact]
    public void IncomingSiegeDamageMul_UsesTenthsOfPercentScale()
    {
        var unit = new Unit();
        var template = new BonusTemplate
        {
            Attribute = UnitAttribute.IncomingSiegeDamageMul,
            ModifierType = UnitModifierType.Value,
            Value = -16
        };

        unit.AddBonus(1, new Bonus
        {
            Owner = unit,
            Template = template,
            Value = template.Value
        });

        Assert.InRange(unit.IncomingSiegeDamageMul, 0.9839f, 0.9841f);
    }
}
