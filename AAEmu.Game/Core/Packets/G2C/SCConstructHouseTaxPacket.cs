using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The tax quote the client asks for before it will let a placement go ahead.
/// </summary>
/// <remarks>
/// Payload is 49 bytes: <c>design:i32, heavyTaxHouseCount:i32, normalTaxHouseCount:i32,
/// isHeavyTaxHouse:bool, four money amounts as i64, hostileTaxRate:i32</c>.
///
/// The four amounts are 64-bit. Writing them as ints made the body 33 bytes instead of 49, so
/// the client read the last three amounts and the hostile rate out of the wrong bytes and had
/// nothing usable to show for the quote.
///
/// The four share a generic label in the client, so their individual meanings are ours rather
/// than proven; the names here are what we fill them with.
/// </remarks>
public class SCConstructHouseTaxPacket : GamePacket
{
    private readonly uint _designId;
    private readonly int _heavyTaxHouseCount;
    private readonly int _normalTaxHouseCount;
    private readonly bool _isHeavyTaxHouse;
    private readonly long _baseTaxMoneyAmount;
    private readonly long _depositTaxMoneyAmount;
    private readonly long _totalTaxMoneyAmount;
    private readonly long _moneyAmount;
    private readonly int _hostileTaxRate;

    public SCConstructHouseTaxPacket(uint designId, int heavyTaxHouseCount, int normalTaxHouseCount,
        bool isHeavyTaxHouse, long baseTaxMoneyAmount, long depositTaxMoneyAmount, long totalTaxMoneyAmount,
        long moneyAmount = 0, int hostileTaxRate = 0)
        : base(SCOffsets.SCConstructHouseTaxPacket, 5)
    {
        _designId = designId;
        _heavyTaxHouseCount = heavyTaxHouseCount;
        _normalTaxHouseCount = normalTaxHouseCount;
        _isHeavyTaxHouse = isHeavyTaxHouse;
        _baseTaxMoneyAmount = baseTaxMoneyAmount;
        _depositTaxMoneyAmount = depositTaxMoneyAmount;
        _totalTaxMoneyAmount = totalTaxMoneyAmount;
        _moneyAmount = moneyAmount;
        _hostileTaxRate = hostileTaxRate;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_designId);              // design (type)
        stream.Write(_heavyTaxHouseCount);    // heavyTaxHouseCount
        stream.Write(_normalTaxHouseCount);   // normalTaxHouseCount
        stream.Write(_isHeavyTaxHouse);       // isHeavyTaxHouse
        stream.Write(_baseTaxMoneyAmount);    // moneyAmount
        stream.Write(_depositTaxMoneyAmount); // moneyAmount
        stream.Write(_totalTaxMoneyAmount);   // moneyAmount
        stream.Write(_moneyAmount);           // moneyAmount
        stream.Write(_hostileTaxRate);        // hostileTaxRate

        return stream;
    }
}
