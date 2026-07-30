using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Echoes a chat message to a recipient. Wire layout matched against 1.8.1.0
/// client captures on opcode 0x2F3 - channelType/subType/factionId (with the
/// factionId repeated), then caster info, message, up to 4 chat links, ability
/// and a fixed 0/0/1 tail. Note the client's own "White" (say) channel numeric
/// value is what this codebase's ChatType enum calls Party; the swap below is
/// intentional and mirrors CSSendChatMessagePacket's inverse remap on read.
/// </summary>
public class SCChatMessagePacket : GamePacket
{
    private readonly ChatType _type;
    private readonly Character _character;
    private readonly string _message;
    private readonly int _ability;
    private readonly byte _languageType;
    private readonly byte[] _linkType;
    private readonly ushort[] _start;
    private readonly ushort[] _lenght;
    private readonly Dictionary<int, byte[]> _data;
    private readonly uint[] _qType;
    private readonly ulong[] _itemId;

    public SCChatMessagePacket(ChatType type, string message, byte[] linkType = null) :
        base(SCOffsets.SCChatMessagePacket, 5)
    {
        _type = type;
        _message = message;
        _linkType = linkType;
    }

    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType = null) :
        base(SCOffsets.SCChatMessagePacket, 5)
    {
        _type = type;
        _character = character;
        _message = message;
        _ability = ability;
        _languageType = languageType;
        _linkType = linkType;
    }

    public SCChatMessagePacket(
        ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType, ushort[] start, ushort[] lenght, Dictionary<int, byte[]> data, uint[] qType, ulong[] itemId) :
        base(SCOffsets.SCChatMessagePacket, 5)
    {
        _type = type;
        _character = character;
        _message = message;
        _ability = ability;
        _languageType = languageType;
        _linkType = linkType;
        _start = start;
        _lenght = lenght;
        _data = data;
        _qType = qType;
        _itemId = itemId;
    }

    public SCChatMessagePacket(ChatType type, string message) : base(SCOffsets.SCChatMessagePacket, 5)
    {
        _type = type;
        _message = message;
    }

    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType) :
        base(SCOffsets.SCChatMessagePacket, 5)
    {
        _type = type;
        _character = character;
        _message = message;
        _ability = ability;
        _languageType = languageType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var channelType = _type == ChatType.White ? ChatType.Party : _type;
        var channelFactionId = _character?.Faction.Id ?? 0;

        stream.Write((short)channelType);
        stream.Write((byte)0); // subType
        stream.Write((ushort)channelFactionId);
        stream.Write(channelFactionId);
        stream.WriteBc(_character?.ObjId ?? 0);
        stream.Write(_character?.Id ?? 0);
        stream.Write(0);
        stream.Write(_character != null ? GetLanguageType(_languageType) : (byte)0);
        stream.Write(_character != null ? (byte)_character.Race : (byte)0);
        stream.Write(channelFactionId);
        if (_character?.Connection?.GetAttribute("gmFlag") != null)
            stream.Write(_character != null ? "GM " + _character.Name : "");
        else
            stream.Write(_character != null ? _character.Name : "");
        stream.Write(_message);

        for (var i = 0; i < 4; i++)
        {
            var linkedType = _linkType?[i] ?? 0;
            stream.Write(linkedType);

            if (linkedType > 0)
            {
                stream.Write(_start[i]);
                stream.Write(_lenght[i]);
                switch (linkedType)
                {
                    case 1:
                        stream.Write(_data[i]);
                        break;
                    case 3:
                        stream.Write(_qType[i]);
                        break;
                    case 4:
                        stream.Write(_itemId[i]);
                        break;
                    case 5:
                        stream.WriteBc(0);
                        break;
                }
            }
        }

        stream.Write(_character != null ? GetAbility(_ability) : 0);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)1);

        return stream;
    }

    private static byte GetLanguageType(byte languageType)
    {
        return languageType != 0 ? languageType : (byte)9;
    }

    private static int GetAbility(int ability)
    {
        return ability != 0 ? ability : 20000;
    }
}
