using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A purchase was refused because the player has already bought their allowance of that good.
/// </summary>
/// <remarks>
/// The kind of allowance decides which message the player sees, and only three values reach a
/// useful one; anything else lands in a branch that says nothing.
/// </remarks>
public class SCBuyFailedMerchantGoodLimitPurchasePacket : GamePacket
{
    /// <summary>How the allowance is counted. Only these reach a message the player can read.</summary>
    public enum PurchaseLimitKind : byte
    {
        Daily = 2,
        Weekly = 3,
        Monthly = 4
    }

    private readonly uint _type;
    private readonly PurchaseLimitKind _purchaseType;
    private readonly uint _purchaseLimit;

    public SCBuyFailedMerchantGoodLimitPurchasePacket(uint type, PurchaseLimitKind purchaseType, uint purchaseLimit)
        : base(SCOffsets.SCBuyFailedMerchantGoodLimitPurchasePacket, 5)
    {
        _type = type;
        _purchaseType = purchaseType;
        _purchaseLimit = purchaseLimit;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_type);              // affected good; exact meaning not recovered
        stream.Write((byte)_purchaseType);
        stream.Write(_purchaseLimit);     // the allowance itself, as the server counts it
        return stream;
    }
}
