using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSelectCharacterPacket : GamePacket
{
    public CSSelectCharacterPacket() : base(CSOffsets.CSSelectCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterId = stream.ReadUInt32();
        var gm = stream.ReadBoolean();
        stream.ReadByte();

        if (Connection.Characters.TryGetValue(characterId, out var character))
        {
            // Despawn any old pets this character might have even before loading it
            //var character = Connection.Characters[characterId];
            character.Load();
            character.Connection = Connection;
            character.DisabledSetPosition = false;
            character.WorldEntryComplete = false;
            var houses = Connection.Houses.Values.Where(x => x.OwnerId == character.Id);
            MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(character);

            Connection.ActiveChar = character;
            if (Character.UsedCharacterObjIds.TryGetValue(character.Id, out var oldObjId))
            {
                Connection.ActiveChar.ObjId = oldObjId;
            }
            else
            {
                Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();
                Character.UsedCharacterObjIds.TryAdd(character.Id, character.ObjId);
            }

            var mySlave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
            if (mySlave != null)
            {
                Logger.Warn($"{Connection.ActiveChar.Name}: Interrupting the transport shutdown task");
                mySlave.CancelTokenSource.Cancel();
            }
            var myMates = MateManager.Instance.GetActiveMates(Connection.ActiveChar.ObjId);
            if (myMates != null)
            {
                Unit.DespawMate(Connection.ActiveChar); // despawn because we lost control over them
            }

            Connection.ActiveChar.Simulation = new Simulation(character);

            // Target Kakao 1.8.1 does not contain the legacy placeholder
            // buff templates 8000011/8000012. Do not inject them into the
            // character before SCUnitState; correct target account buffs will
            // be restored later from the target account_buffs data.


            Connection.SendPacket(new SCCharacterStatePacket(character));
            // The abilityExp array that just went out carries what the forms are worth, but not
            // that the character has them: the client builds its learned-ability record from the
            // announcement below and from nothing else.
            Connection.ActiveChar.Abilities.SendSpecialAbilities();
            // SCCharacterGamePoints is 0x05A in the target DLL. The legacy class is
            // still mapped to 0x2EE and is unsafe until its 10.8 serializer is rebuilt.
            Connection.ActiveChar.Inventory.Send();
            Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

            // Both completed history and active contexts must be registered while
            // the target client is still building the quest journal index. Sending
            // completed bitsets only at CSWorldEntryReady correctly hid rewarded
            // quests, but their chapter/step labels had already been built as "???".
            // Target order: completed history first, then active contexts, then notifier.
            Connection.ActiveChar.Quests.SendCompleted();
            Connection.ActiveChar.Quests.Send();
            Connection.ActiveChar.Quests.RefreshQuestNotifier();
            Connection.ActiveChar.Quests.RecallEvents();

            Connection.ActiveChar.Actability.Send();
            Connection.ActiveChar.Mails.SendUnreadMailCount();
            Connection.ActiveChar.Appellations.Send();
            Connection.ActiveChar.Portals.Send();
            Connection.ActiveChar.Friends.Send();
            Connection.ActiveChar.Blocked.Send();

            // The crafting window builds its pinned list from this and from nothing else, so a
            // player who pinned anything sees an empty list until it arrives.
            Connection.ActiveChar.FavoriteCrafts.Send();

            // The owned-house summary. This used to go out as one message per building, which
            // does not exist in this client - it was a leftover from an older version and
            // carried a placeholder opcode. The target sends this summary instead, and the
            // client caps it at twenty records, so larger sets are split across messages.
            // Nothing to announce when the player owns nothing - an empty summary tells the
            // client only that it has no buildings, which it already assumes.
            var ownedHouses = houses.ToList();
            for (var offset = 0; offset < ownedHouses.Count; offset += SCHouseDataPacket.MaxRecords)
            {
                var chunk = ownedHouses.GetRange(offset,
                    Math.Min(SCHouseDataPacket.MaxRecords, ownedHouses.Count - offset));
                Connection.SendPacket(new SCHouseDataPacket(chunk));
            }

            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                Connection.SendPacket(new SCConflictZoneStatePacket(conflict.ZoneGroupId, conflict.CurrentZoneState, conflict.NextStateTime));
            }

            FactionManager.Instance.SendFactions(Connection.ActiveChar);
            FactionManager.Instance.SendRelations(Connection.ActiveChar);
            ExpeditionManager.Instance.SendExpeditions(Connection.ActiveChar);

            if (Connection.ActiveChar.Expedition != null)
            {
                ExpeditionManager.SendExpeditionInfo(Connection.ActiveChar);
            }

            Connection.ActiveChar.SendOption(1);
            Connection.ActiveChar.SendOption(2);
            Connection.ActiveChar.SendOption(5);

            Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.LoggedOn, Connection.ActiveChar);

            var template = CharacterManager.Instance.GetTemplate((byte)character.Race, (byte)character.Gender);

            foreach (var buff in template.Buffs)
            {
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(buff);
                var casterObj = new SkillCasterUnit(character.ObjId);
                character.Buffs.AddBuff(new Buff(character, character, casterObj, buffTemplate, null, DateTime.UtcNow) { Passive = true });
            }

            character.Breath = character.LungCapacity;

            Connection.ActiveChar.OnZoneChange(0, Connection.ActiveChar.Transform.ZoneId);
        }
        else
        {
            // TODO ...
        }
    }
}
