using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterSkills
{
    private enum SkillType : byte
    {
        Skill = 1,
        Buff = 2
    }

    private List<uint> _removed;

    public Dictionary<uint, Skill> Skills { get; set; }
    public Dictionary<uint, PassiveBuff> PassiveBuffs { get; set; }

    public Character Owner { get; set; }

    public CharacterSkills(Character owner)
    {
        Owner = owner;
        Skills = new Dictionary<uint, Skill>();
        PassiveBuffs = new Dictionary<uint, PassiveBuff>();
        _removed = new List<uint>();
    }

    public void AddSkill(uint skillId)
    {
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return;

        if (template.AbilityId > 0 &&
            template.AbilityId != (byte)Owner.Ability1 &&
            template.AbilityId != (byte)Owner.Ability2 &&
            template.AbilityId != (byte)Owner.Ability3)
            return;

        if (template.NeedLearn)
        {
            var availablePoints = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level) - GetUsedSkillPoints();
            if (template.SkillPoints > availablePoints)
                return;
        }

        if (Skills.TryGetValue(skillId, out var learnedSkill))
        {
            Owner.SendPacket(new SCSkillLearnedPacket(learnedSkill));
            return;
        }

        AddSkill(template, CalculateSkillLevel(template), true);
    }

    public void AddSkill(SkillTemplate template, byte level, bool packet)
    {
        if (template == null || Skills.ContainsKey(template.Id))
            return;

        var skill = new Skill(template, Owner)
        {
            Level = level > 0 ? level : CalculateSkillLevel(template)
        };
        Skills.Add(skill.Id, skill);

        if (packet)
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
    }

    public void AddBuff(uint buffId)
    {
        var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
        if (template == null)
            return;

        if (template.AbilityId > 0 &&
            template.AbilityId != (byte)Owner.Ability1 &&
            template.AbilityId != (byte)Owner.Ability2 &&
            template.AbilityId != (byte)Owner.Ability3)
            return;

        var availablePoints = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level) - GetUsedSkillPoints();
        if (template.ReqPoints > availablePoints || PassiveBuffs.ContainsKey(buffId))
            return;

        AddPassiveBuff(template, true);
    }

    private void AddPassiveBuff(PassiveBuffTemplate template, bool packet)
    {
        if (template == null || PassiveBuffs.ContainsKey(template.Id))
            return;

        var buff = new PassiveBuff { Id = template.Id, Template = template };
        PassiveBuffs.Add(buff.Id, buff);
        if (packet)
            Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id));
        buff.Apply(Owner);
    }

    private byte CalculateSkillLevel(SkillTemplate template)
    {
        if (template.LevelStep <= 0)
            return 1;

        var abilityLevel = Owner.GetAbLevel((AbilityType)template.AbilityId);
        var calculated = ((abilityLevel - template.AbilityLevel) / template.LevelStep) + 1;
        return (byte)Math.Clamp(calculated, 1, byte.MaxValue);
    }

    public void Reset(AbilityType abilityId, bool notify) // TODO with price...
    {
        foreach (var skill in new List<Skill>(Skills.Values))
        {
            if (skill.Template.AbilityId != (byte)abilityId)
                continue;
            Skills.Remove(skill.Id);
            _removed.Add(skill.Id);
        }

        foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
        {
            if (buff.Template.AbilityId != (byte)abilityId)
                continue;
            buff.Remove(Owner);
            PassiveBuffs.Remove(buff.Id);
            Owner.Buffs.RemoveBuff(buff.Id);
            _removed.Add(buff.Id);
        }

        if (notify)
            Owner.SendPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId));
    }

    public int GetUsedSkillPoints()
    {
        var points = 0;
        foreach (var skill in Skills.Values)
            points += skill.Template.SkillPoints;
        foreach (var buff in PassiveBuffs.Values)
            points += buff.Template?.ReqPoints ?? 1;
        return points;
    }

    // TODO : Optimize this by storing a map of derivative skills and their matches
    public bool IsVariantOfSkill(uint skillId)
    {
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
        if (skillTemplate == null)
            return false;

        return Skills.Values.Any(skill =>
            skill.Template != null &&
            skill.Template.AbilityId == skillTemplate.AbilityId &&
            skill.Template.AbilityLevel == skillTemplate.AbilityLevel);
    }

    #region database
    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT `id`, `level`, `type` FROM skills WHERE `owner` = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Enum.TryParse(reader.GetString("type"), true, out SkillType type))
                continue;

            var id = reader.GetUInt32("id");
            switch (type)
            {
                case SkillType.Skill:
                {
                    var template = SkillManager.Instance.GetSkillTemplate(id);
                    if (template != null)
                        AddSkill(template, reader.GetByte("level"), false);
                    break;
                }
                case SkillType.Buff:
                {
                    var template = SkillManager.Instance.GetPassiveBuffTemplate(id);
                    if (template != null)
                        AddPassiveBuff(template, false);
                    break;
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "DELETE FROM skills WHERE owner = @owner AND id IN(" + string.Join(",", _removed) + ")";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
                _removed.Clear();
            }
        }

        foreach (var skill in Skills.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (@id, @level, @type, @owner)";
                command.Parameters.AddWithValue("@id", skill.Id);
                command.Parameters.AddWithValue("@level", skill.Level);
                command.Parameters.AddWithValue("@type", (byte)SkillType.Skill);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }

        foreach (var buff in PassiveBuffs.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES(@id,@level,@type,@owner)";
                command.Parameters.AddWithValue("@id", buff.Id);
                command.Parameters.AddWithValue("@level", 1);
                command.Parameters.AddWithValue("@type", (byte)SkillType.Buff);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }

    #endregion
}
