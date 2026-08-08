using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterAbilities
{
    public Dictionary<AbilityType, Ability> Abilities { get; set; }
    public Character Owner { get; set; }

    public CharacterAbilities(Character owner)
    {
        Owner = owner;
        Abilities = new Dictionary<AbilityType, Ability>();

        // Target 1.8.1.0 keeps a fixed 29-record ability table on the character.
        // Keep all wire slots addressable because special mutation skills still carry ability ids
        // 28/29. Only ids 1..14 are normal selectable/player-XP skillsets.
        for (var i = 1; i < (int)AbilityType.None; i++)
        {
            var id = (AbilityType)i;
            Abilities[id] = new Ability(id);
        }
    }

    public IEnumerable<Ability> Values => Abilities.Values;

    public void SetAbility(AbilityType id, byte order)
    {
        if (!id.IsPlayerSkillset() || !Abilities.TryGetValue(id, out var ability))
            return;

        ability.Order = order;
    }

    public List<AbilityType> GetActiveAbilities()
    {
        var list = new List<AbilityType>();
        if (Owner.Ability1.IsPlayerSkillset())
            list.Add(Owner.Ability1);
        if (Owner.Ability2.IsPlayerSkillset())
            list.Add(Owner.Ability2);
        if (Owner.Ability3.IsPlayerSkillset())
            list.Add(Owner.Ability3);
        return list;
    }

    public void AddExp(AbilityType type, int exp)
    {
        // TODO SCAbilityExpChangedPacket
        if (type.IsPlayerSkillset() && Abilities.TryGetValue(type, out var ability))
            ability.Exp += exp;
    }

    public void AddActiveExp(int exp)
    {
        // TODO SCExpChangedPacket
        AddActiveAbilityExp(Owner.Ability1, exp);
        AddActiveAbilityExp(Owner.Ability2, exp);
        AddActiveAbilityExp(Owner.Ability3, exp);
    }

    private void AddActiveAbilityExp(AbilityType id, int exp)
    {
        if (!id.IsPlayerSkillset() || !Abilities.TryGetValue(id, out var ability))
            return;

        ability.Exp = Math.Min(ability.Exp + exp, ExperienceManager.Instance.GetExpForLevel(55));
    }

    public void Swap(AbilityType oldAbilityId, AbilityType abilityId)
    {
        // 15..27 are reserved; 28/29 are learned through SpecialAbility, not the
        // ordinary three-skillset selection. Do not let them enter Ability1..3.
        if (!abilityId.IsPlayerSkillset())
            return;
        if (oldAbilityId != AbilityType.None && !oldAbilityId.IsPlayerSkillset())
            return;

        Owner.Skills.Reset(oldAbilityId, true);
        if (Owner.Ability1 == oldAbilityId)
        {
            Owner.Ability1 = abilityId;
            Abilities[abilityId].Order = 0;
        }
        else if (Owner.Ability2 == oldAbilityId)
        {
            Owner.Ability2 = abilityId;
            Abilities[abilityId].Order = 1;

            //This sets are current ability level to match ability1 since its suppost to be in sync
            if (oldAbilityId == AbilityType.None)
            {
                Abilities[Owner.Ability2].Exp = Abilities[Owner.Ability1].Exp;
            }
        }
        else if (Owner.Ability3 == oldAbilityId)
        {
            Owner.Ability3 = abilityId;
            Abilities[abilityId].Order = 2;

            if (oldAbilityId == AbilityType.None)
            {
                Abilities[Owner.Ability3].Exp = Abilities[Owner.Ability1].Exp;

                //every unchosen ability is default level 10 besides are selected ones since spillover exp can unsync character exp with skill exp
                var active = GetActiveAbilities();
                foreach (var ability in Abilities.Values)
                {
                    if (ability.Id.IsPlayerSkillset() && !active.Contains(ability.Id))
                        ability.Exp = 42000;
                }
            }
        }

        if (oldAbilityId != AbilityType.None)
            Abilities[oldAbilityId].Order = 255;
        Owner.BroadcastPacket(new SCAbilitySwappedPacket(Owner.ObjId, oldAbilityId, abilityId), true);
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM abilities WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var ability = new Ability
                    {
                        Id = (AbilityType)reader.GetByte("id"),
                        Exp = reader.GetInt32("exp")
                    };
                    if ((byte)ability.Id <= (byte)AbilityType.General || (byte)ability.Id >= (byte)AbilityType.None)
                        continue;
                    if (ability.Id == Owner.Ability1)
                        ability.Order = 0;
                    if (ability.Id == Owner.Ability2)
                        ability.Order = 1;
                    if (ability.Id == Owner.Ability3)
                        ability.Order = 2;
                    Abilities[ability.Id] = ability;
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var ability in Abilities.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO abilities(`id`,`exp`,`owner`) VALUES (@id, @exp, @owner)";
                command.Parameters.AddWithValue("@id", (byte)ability.Id);
                command.Parameters.AddWithValue("@exp", ability.Exp);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }
}
