using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A trade with a vendor did not go through.
/// </summary>
/// <remarks>
/// The store window keeps its own state - a pending purchase or sale it is waiting on - and this
/// is what releases it and names which side failed. A general error message may print something,
/// but it leaves that state where it was, which is why a refused sale left the window stuck.
/// </remarks>
public class SCStoreTradeFailedPacket : GamePacket
{
    private readonly bool _buy;

    /// <param name="buy">True for a failed purchase, false for a failed sale.</param>
    public SCStoreTradeFailedPacket(bool buy) : base(SCOffsets.SCStoreTradeFailedPacket, 5)
    {
        _buy = buy;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_buy);
        return stream;
    }
}
