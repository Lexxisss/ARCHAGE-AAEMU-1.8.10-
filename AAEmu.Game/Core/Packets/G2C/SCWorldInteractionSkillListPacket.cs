using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_WORLD_INTERACTION_SKILL_LIST (0x01F7).
/// Wire layout is taken from the target x2game.dll serializers at
/// 0x399EB140 (packet) and 0x399E60B0 (WorldInteractionList).
/// </summary>
public class SCWorldInteractionSkillListPacket : GamePacket
{
    private readonly uint _targetObjId;
    private readonly uint _sourceObjId;
    private readonly uint _interactionType;
    private readonly uint _pickObjId;
    private readonly uint _extraInfo;
    private readonly byte _mouseButton;
    private readonly uint _modifierKeys;
    private readonly uint[] _interactions;

    public SCWorldInteractionSkillListPacket(
        uint targetObjId,
        uint sourceObjId,
        uint extraInfo,
        uint pickObjId,
        byte mouseButton,
        uint modifierKeys,
        uint[] interactions,
        uint interactionType = 0)
        : base(SCOffsets.SCWorldInteractionSkillListPacket, 5)
    {
        _targetObjId = ToBc(targetObjId);
        _sourceObjId = ToBc(sourceObjId);
        _interactionType = interactionType;
        _pickObjId = ToBc(pickObjId);
        _extraInfo = extraInfo;
        _mouseButton = mouseButton;
        _modifierKeys = modifierKeys;
        _interactions = (interactions ?? Array.Empty<uint>()).Take(10).ToArray();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Packet fields +0x10/+0x14.
        stream.WriteBc(_targetObjId);
        stream.WriteBc(_sourceObjId);

        // Embedded WorldInteractionList.
        stream.WriteBc(_targetObjId);  // +0x00
        stream.WriteBc(_sourceObjId);  // +0x04
        stream.Write(_interactionType); // +0x08
        stream.WriteBc(_pickObjId);    // +0x0C
        stream.Write((uint)_interactions.Length); // +0x38
        stream.Write(_extraInfo);      // +0x3C
        foreach (var interaction in _interactions)
            stream.Write(interaction);

        stream.Write(_mouseButton); // packet +0x58
        stream.Write(_modifierKeys); // packet +0x5C
        return stream;
    }

    private static uint ToBc(uint value)
    {
        return value <= 0x00FF_FFFF ? value : 0;
    }
}
