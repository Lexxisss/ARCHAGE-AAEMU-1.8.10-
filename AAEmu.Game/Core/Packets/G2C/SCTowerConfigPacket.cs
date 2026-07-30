using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8.1.0 bootstrap configuration packet (0x01E0).
/// The working reference sends an empty configuration array, encoded as a
/// 32-bit entry count of zero. This is serialized as a real typed packet;
/// no captured packet body is used.
/// </summary>
public sealed class SCTowerConfigPacket : GamePacket
{
    private readonly uint _entryCount;

    /// <summary>
    /// Creates the reference bootstrap form: an empty configuration array.
    /// </summary>
    public SCTowerConfigPacket() : this(0)
    {
    }

    private SCTowerConfigPacket(uint entryCount)
        : base(SCOffsets.SCTowerConfigPacket, 5)
    {
        _entryCount = entryCount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_entryCount);
        return stream;
    }
}
