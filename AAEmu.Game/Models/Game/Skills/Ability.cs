namespace AAEmu.Game.Models.Game.Skills;

public enum AbilityType : byte
{
    General = 0,
    Fight = 1,
    Illusion = 2,
    Adamant = 3,
    Will = 4,
    Death = 5,
    Wild = 6,
    Magic = 7,
    Vocation = 8,
    Romance = 9,
    Love = 10,

    // Target 1.8.1.0 client ability-name mapper:
    // hatred=11, assassin=12, madness=13, pleasure=14.
    Hatred = 11,
    Assassin = 12,
    Madness = 13,  // Gunslinger
    Pleasure = 14, // Spelldance

    // 15..27 are real protocol slots, but the client names them space4..space16
    // and does not expose them as ordinary selectable skillsets.
    Space4 = 15,
    Space5 = 16,
    Space6 = 17,
    Space7 = 18,
    Space8 = 19,
    Space9 = 20,
    Space10 = 21,
    Space11 = 22,
    Space12 = 23,
    Space13 = 24,
    Space14 = 25,
    Space15 = 26,
    Space16 = 27,

    // Live special/mutation abilities. The client learns them through a separate
    // SpecialAbility path rather than the normal three selectable skillsets.
    Predator = 28,
    Trooper = 29,

    None = 30
}

public static class AbilityTypeExtensions
{
    public static bool IsPlayerSkillset(this AbilityType ability)
    {
        return (byte)ability >= (byte)AbilityType.Fight && (byte)ability <= (byte)AbilityType.Pleasure;
    }

    public static bool IsSpecialAbility(this AbilityType ability)
    {
        return ability is AbilityType.Predator or AbilityType.Trooper;
    }
}

public class Ability
{
    public AbilityType Id { get; set; }
    public byte Order { get; set; }
    public int Exp { get; set; }

    /// <summary>
    /// Whether a special ability has been learned. Meaningless for an ordinary skillset.
    /// </summary>
    /// <remarks>
    /// The client keeps this apart from progression, and experience cannot stand in for it: a
    /// form can be learned while its experience is still zero.
    /// </remarks>
    public bool Learned { get; set; }

    public Ability()
    {
        Order = 255;
    }

    public Ability(AbilityType id)
    {
        Id = id;
        Order = 255;
    }
}
