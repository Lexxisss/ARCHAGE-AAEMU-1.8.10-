using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Synchronizes the mutable part of an already-created buff.
/// Target x2game.dll names the fields targetId, buffId, stack, charged,
/// elapsedTime and reason; decoded 10.x wire bodies are 20 bytes when the
/// target object id uses the normal three-byte Bc representation.
/// </summary>
public class SCBuffUpdatedPacket : GamePacket
{
    private readonly Buff _buff;
    private readonly byte _reason;

    public SCBuffUpdatedPacket(Buff buff, byte reason = 1) : base(SCOffsets.SCBuffUpdatedPacket, 5)
    {
        _buff = buff;
        _reason = reason;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_buff.Owner.ObjId);       // targetId
        stream.Write(_buff.Index);               // buffId (runtime buff index)
        stream.Write(_buff.StackCount);               // stack
        stream.Write(_buff.Charge);              // charged
        stream.Write(_buff.GetTimeElapsed());    // elapsedTime
        stream.Write(_reason);                   // 1 in initial visibility captures
        return stream;
    }
}
