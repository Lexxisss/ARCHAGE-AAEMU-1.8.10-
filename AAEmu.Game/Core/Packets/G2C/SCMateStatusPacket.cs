using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Pet / mount state.
/// </summary>
/// <remarks>
/// The handler looks the unit up and silently drops the whole skill state when it is missing,
/// so this must not be sent before the world object exists.
///
/// The skill state carries three collections - skills, tags and charges - and the charge count
/// was missing, leaving the packet short.
/// </remarks>
public class SCMateStatusPacket : GamePacket
{
    private readonly uint _objId;
    private readonly int _skillCount;
    private readonly int _tagCount;
    private readonly int _chargeCount;

    public SCMateStatusPacket(uint objId) : base(SCOffsets.SCMateStatusPacket, 5)
    {
        _objId = objId;
        _skillCount = 0;
        _tagCount = 0;
        _chargeCount = 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId); // unitId : compressed

        // SkillState: three separate collections, each a count followed by that many triples.
        stream.Write(_skillCount);
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(0u);
            stream.Write(0u);
            stream.Write(0u);
        }

        stream.Write(_tagCount);
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u);
            stream.Write(0u);
            stream.Write(0u);
        }

        stream.Write(_chargeCount);
        for (var i = 0; i < _chargeCount; i++)
        {
            stream.Write(0u);
            stream.Write(0u);
            stream.Write(0u);
        }

        return stream;
    }
}
