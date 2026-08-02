using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Authoritative tax state for one house.
/// </summary>
/// <remarks>
/// Every field here is pushed by the server: the client does not derive the bill or the due
/// date from its own data. Overdue state is <see cref="_weeksWithoutPay"/> and prepayment is
/// <see cref="_weeksPrepay"/>.
///
/// Four widths were wrong and each shifted what followed it: <c>tl</c> is 32 bits and was
/// written as 16, the two money fields are 64 bits and were written as 32, and the two week
/// counters are single bytes and were written as ints. The trailing tax type was missing
/// entirely.
/// </remarks>
public class SCHouseTaxInfoPacket : GamePacket
{
    private readonly uint _tl;
    private readonly int _dominionTaxRate;
    private readonly int _hostileTaxRate;
    private readonly long _moneyAmount;
    private readonly long _moneyAmount2;
    private readonly DateTime _due;
    private readonly bool _isAlreadyPaid;
    private readonly byte _weeksWithoutPay;
    private readonly byte _weeksPrepay;
    private readonly bool _isHeavyTaxHouse;
    private readonly byte _taxType;

    public SCHouseTaxInfoPacket(uint tl, int dominionTaxRate, int hostileTaxRate, long moneyAmount,
        long moneyAmount2, DateTime due, bool isAlreadyPaid, byte weeksWithoutPay, byte weeksPrepay,
        bool isHeavyTaxHouse, byte taxType = 1) : base(SCOffsets.SCHouseTaxInfoPacket, 5)
    {
        _tl = tl;
        _dominionTaxRate = dominionTaxRate;
        _hostileTaxRate = hostileTaxRate;
        _moneyAmount = moneyAmount;
        _moneyAmount2 = moneyAmount2;
        _due = due;
        _isAlreadyPaid = isAlreadyPaid;
        _weeksWithoutPay = weeksWithoutPay;
        _weeksPrepay = weeksPrepay;
        _isHeavyTaxHouse = isHeavyTaxHouse;
        _taxType = taxType;
    }

    /// <remarks>
    /// Forty-one bytes, and every width and position here has been checked against the client and
    /// found to agree. Do not change them. The panel showing a bare number that is none of these is
    /// not this message's doing - both amounts are turned into text before they reach the panel, so
    /// a raw integer on screen comes from somewhere else in its own plumbing.
    /// </remarks>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);               // tl               : u32
        stream.Write(_dominionTaxRate);  // dominionTaxRate  : i32
        stream.Write(_hostileTaxRate);   // hostileTaxRate   : i32
        stream.Write(_moneyAmount);      // moneyAmount0     : i64
        stream.Write(_moneyAmount2);     // moneyAmount1     : i64
        stream.Write(_due);              // due              : u64
        stream.Write(_isAlreadyPaid);    // isAlreadyPaid    : bool
        stream.Write(_weeksWithoutPay);  // weeksWithoutPay  : u8
        stream.Write(_weeksPrepay);      // weeksPrepay      : u8
        stream.Write(_isHeavyTaxHouse);  // isHeavyTaxHouse  : bool
        // One byte, not four. Ordinary buildings are type one; two and three exist and change how
        // a sum is turned into the number of certificates it costs.
        stream.Write(_taxType);          // taxType          : u8
        return stream;
    }
}
