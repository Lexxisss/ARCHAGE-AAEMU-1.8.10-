using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Wire layout matches the target 1.8.1.0 client (opcode 0x027): type/subType/
/// factionId, targetName, a "00 FF" marker byte pair, message, languageType,
/// ability, then up to 4 chat-link blocks (item/quest/recruit/plain-text links).
/// </summary>
public class CSSendChatMessagePacket : GamePacket
{
    public CSSendChatMessagePacket() : base(CSOffsets.CSSendChatMessagePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var type = (ChatType)stream.ReadInt16();
        var subType = stream.ReadInt16();
        var factionId = stream.ReadUInt32();

        var targetName = stream.ReadString();
        ReadChatMarker(stream);
        var message = stream.ReadString();
        var languageType = stream.ReadByte();
        var ability = stream.ReadInt32();

        for (var i = 0; i < 4; i++)
        {
            var linkType = stream.ReadByte();
            if (linkType <= 0)
                continue;

            stream.ReadInt16(); // start
            stream.ReadInt16(); // length
            switch (linkType)
            {
                case 1:
                    stream.ReadBytes(208); // plain-text link data
                    break;
                case 3:
                    stream.ReadInt32(); // quest link qType
                    break;
                case 4:
                    stream.ReadInt64(); // item link itemId
                    break;
                case 5:
                    stream.ReadBc(); // recruit link
                    break;
            }
        }

        type = NormalizeChatType(type, subType, factionId, targetName);

        Logger.Debug(
            "ChatMessage: type={0}, subType={1}, factionId={2}, targetName='{3}', message='{4}', left={5}",
            type,
            subType,
            factionId,
            targetName,
            message,
            stream.LeftBytes);

        if (message.StartsWith(CommandManager.CommandPrefix))
        {
            if (CommandManager.Instance.Handle(Connection.ActiveChar, message.Substring(CommandManager.CommandPrefix.Length).Trim(), out _))
                return;
        }

        // Sidenote: Trino mixed up /faction and /nation back then, it was supposed to be the other way around
        switch (type)
        {
            case ChatType.Whisper: //whisper
                var target = WorldManager.Instance.GetCharacter(targetName);
                if ((target == null) || (!target.IsOnline))
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.WhisperNoTarget);
                }
                else
                if (target.Faction.MotherId != Connection.ActiveChar.Faction.MotherId)
                {
                    // TODO: proper hostile check
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatCannotWhisperToHostile);
                }
                else
                {
                    var packet = new SCChatMessagePacket(ChatType.Whisper, Connection.ActiveChar, message, ability, languageType);
                    target.SendPacket(packet);
                    var packet_me = new SCChatMessagePacket(ChatType.Whispered, target, message, ability, languageType);
                    Connection.SendPacket(packet_me);
                }
                break;
            case ChatType.White: //say
                Connection.ActiveChar.BroadcastPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), true);
                break;
            case ChatType.RaidLeader:
            case ChatType.Raid:
                var teamRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);

                if (teamRaid != null)
                {
                    if ((type == ChatType.RaidLeader) && (teamRaid.OwnerId != Connection.ActiveChar.Id))
                    {
                        Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotRaidOwner);
                    }
                    else
                    {
                        ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
                    }
                }
                else
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInRaid);
                }
                break;
            case ChatType.Party:
                var partyRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);
                if (partyRaid != null)
                {
                    ChatManager.Instance.GetPartyChat(partyRaid, Connection.ActiveChar).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    // The 1.8.1.0 client sends type=Party for normal say chat when not in
                    // a party under some conditions; fall back to White instead of erroring.
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(ChatType.White, Connection.ActiveChar, message, ability, languageType),
                        true);
                }
                break;
            case ChatType.Trade: //trade
            case ChatType.GroupFind: //lfg
            case ChatType.Shout: //shout
                // We use SendPacket here so we can fake our way through the different channel types
                ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).SendPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                    );
                break;
            case ChatType.Clan:
                if (Connection.ActiveChar.Expedition != null)
                {
                    ChatManager.Instance.GetGuildChat(Connection.ActiveChar.Expedition).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    // Looks like the client blocks the chat even before it can get to the server, but let's intercept it anyway
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInExpedition);
                }
                break;
            case ChatType.Family:
                if (Connection.ActiveChar.Family > 0)
                {
                    ChatManager.Instance.GetFamilyChat(Connection.ActiveChar.Family).SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    // Looks like the client blocks the chat even before it can get to the server, but let's intercept it anyway
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInFamily);
                }
                break;
            /*
        case ChatType.Judge:
            // TODO: Need a check so only defendant and jury can talk here, the client does some checks too, but let's make sure
            ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendPacket(
                new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType)
                );
            break;
            */
            case ChatType.Region: //nation (birth place/race, includes pirates etc)
                ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race).SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;
            case ChatType.Ally: //faction (by current allegiance)
                ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId).SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;
            default:
                Logger.Warn("Unsupported chat type {0} from {1}", type, Connection.ActiveChar.Name);
                break;
        }
    }

    private void ReadChatMarker(PacketStream stream)
    {
        if (stream.LeftBytes >= 2 && stream.Buffer[stream.Pos] == 0x00 && stream.Buffer[stream.Pos + 1] == 0xFF)
        {
            stream.ReadByte();
            stream.ReadByte();
            return;
        }

        Logger.Warn(
            "ChatMessage: expected 1.8 chat marker 00 FF after targetName, pos={0}, left={1}",
            stream.Pos,
            stream.LeftBytes);
    }

    private static ChatType NormalizeChatType(ChatType parsedType, short parsedSubType, uint parsedFactionId, string parsedTargetName)
    {
        if (parsedType == ChatType.Party &&
            parsedSubType == 0 &&
            parsedFactionId == 0 &&
            string.IsNullOrEmpty(parsedTargetName))
        {
            return ChatType.White;
        }

        if (parsedType == ChatType.Trade &&
            parsedSubType == 0 &&
            parsedFactionId == 0 &&
            string.IsNullOrEmpty(parsedTargetName))
        {
            return ChatType.White;
        }

        return parsedType;
    }
}
