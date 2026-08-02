using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The plants and plots a player currently has on common farm land.
/// </summary>
/// <remarks>
/// Each record carries both <c>plantTime</c> and a growth value, which is enough for the
/// client to run its own timer from the server's start value. Whether every stage transition
/// also has to be pushed is not established.
///
/// The position is the 11-byte compact form - four bytes of x, four of y, three of z - not
/// the 20-byte world position the house state block uses.
///
/// <c>currentPhase</c> is an optional group and this writes nothing for it, so every record
/// goes out as "no phase". If that group turns out to be introduced by a presence flag rather
/// than simply omitted, the flag belongs here and the record shifts without it.
/// </remarks>
public class SCResponseCommonFarmListPacket : GamePacket
{
    private const int MaxRecords = 64;

    private readonly int _maxCount;
    private readonly IReadOnlyList<CommonFarmPlant> _plants;

    public SCResponseCommonFarmListPacket(int maxCount, IReadOnlyList<CommonFarmPlant> plants = null)
        : base(SCOffsets.SCResponseCommonFarmListPacket, 5)
    {
        _maxCount = maxCount;
        _plants = plants ?? Array.Empty<CommonFarmPlant>();
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(_plants.Count, MaxRecords);

        stream.Write(_maxCount); // maxCount : i32
        stream.Write(count);     // count    : i32, the client caps this at 64

        for (var i = 0; i < count; i++)
        {
            var plant = _plants[i];
            stream.Write(plant.Type0);     // type0     : i32
            stream.Write(plant.Type1);     // type1     : i32
            stream.Write(plant.Growing);   // growing   : i32
            stream.WriteWorldPosition(plant.X, plant.Y, plant.Z);
            stream.Write(plant.PlantTime); // plantTime : u64
        }

        return stream;
    }
}
