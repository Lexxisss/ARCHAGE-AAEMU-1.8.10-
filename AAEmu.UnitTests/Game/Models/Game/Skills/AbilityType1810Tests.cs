using System.Linq;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class AbilityType1810Tests
{
    [Fact]
    public void TargetAbilityIdsMatchRecoveredClientMapper()
    {
        Assert.Equal(11, (byte)AbilityType.Hatred);
        Assert.Equal(12, (byte)AbilityType.Assassin);
        Assert.Equal(13, (byte)AbilityType.Madness);
        Assert.Equal(14, (byte)AbilityType.Pleasure);
        Assert.Equal(28, (byte)AbilityType.Predator);
        Assert.Equal(29, (byte)AbilityType.Trooper);
        Assert.Equal(30, (byte)AbilityType.None);
    }

    [Fact]
    public void OnlyOrdinarySkillsetsAreSelectable()
    {
        Assert.True(AbilityType.Fight.IsPlayerSkillset());
        Assert.True(AbilityType.Madness.IsPlayerSkillset());
        Assert.True(AbilityType.Pleasure.IsPlayerSkillset());
        Assert.False(AbilityType.Space4.IsPlayerSkillset());
        Assert.False(AbilityType.Predator.IsPlayerSkillset());
        Assert.False(AbilityType.Trooper.IsPlayerSkillset());
        Assert.True(AbilityType.Predator.IsSpecialAbility());
        Assert.True(AbilityType.Trooper.IsSpecialAbility());
    }

    [Fact]
    public void CharacterAbilityTableStillKeepsAllTwentyNineWireSlots()
    {
        var character = new Character(new UnitCustomModelParams());
        var abilities = new CharacterAbilities(character);

        Assert.Equal(29, abilities.Abilities.Count);
        Assert.True(abilities.Abilities.ContainsKey(AbilityType.Madness));
        Assert.True(abilities.Abilities.ContainsKey(AbilityType.Pleasure));
        Assert.True(abilities.Abilities.ContainsKey(AbilityType.Predator));
        Assert.True(abilities.Abilities.ContainsKey(AbilityType.Trooper));
        Assert.Equal(14, abilities.Abilities.Keys.Count(x => x.IsPlayerSkillset()));
    }
}
