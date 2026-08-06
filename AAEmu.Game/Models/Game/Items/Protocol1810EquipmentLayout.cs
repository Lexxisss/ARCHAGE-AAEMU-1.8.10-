using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Maps AAEmu's long-standing server equipment slots to the target 1.8.1.0
/// equipment array used by SCUnitState and equipment update packets.
/// </summary>
/// <remarks>
/// Slots Head through Cosplay are already identical (0..27). The target keeps
/// three unnamed positions after Cosplay and places CosplayLooks/RaceCosplay/
/// RaceCosplayLooks at 31/32/33. AAEmu historically stored those three named
/// slots at 28/29/30, so the late range must be permuted only at the wire
/// boundary. Changing EquipmentItemSlot itself would reinterpret persisted
/// character slots and break normal inventory code.
///
/// Target x2game.dll serializer 0x3996AB80 iterates exactly 35 positions:
/// body-part references are 19..25 and NPC full-item records are 27,31,32,33.
/// </remarks>
public static class Protocol1810EquipmentLayout
{
    public const int SlotCount = 35;

    private static readonly byte[] ServerToWire =
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        20, 21, 22, 23, 24, 25, 26, 27,
        31, // server CosplayLooks/Stabilizer -> target internal slot 31
        32, // server RaceCosplay          -> target internal slot 32
        33, // server RaceCosplayLooks     -> target internal slot 33
        28, // server ProtocolSlot31       -> target unnamed slot 28
        29, // server ProtocolSlot32       -> target unnamed slot 29
        30, // server ProtocolSlot33       -> target unnamed slot 30
        34  // server ProtocolSlot34       -> target final unnamed slot
    };

    private static readonly byte[] WireToServer = BuildInverse();

    public static int ToWireSlot(int serverSlot)
    {
        if ((uint)serverSlot >= ServerToWire.Length)
            throw new ArgumentOutOfRangeException(nameof(serverSlot), serverSlot, "Equipment slot is outside the 35-slot server range");
        return ServerToWire[serverSlot];
    }

    public static int ToServerSlot(int wireSlot)
    {
        if ((uint)wireSlot >= WireToServer.Length)
            throw new ArgumentOutOfRangeException(nameof(wireSlot), wireSlot, "Equipment slot is outside the 35-slot target range");
        return WireToServer[wireSlot];
    }

    public static bool IsBodyPartWireSlot(int wireSlot) => wireSlot is >= 19 and <= 25;

    public static bool IsNpcFullItemWireSlot(int wireSlot) => wireSlot == 27 || wireSlot is >= 31 and <= 33;

    /// <summary>
    /// Converts AAEmu's persisted/server slot to the exact 35-qword
    /// SCUnitEquipmentIds wire index. The target 10.8.1.0 packet has no
    /// leading ES_INVALID entry: qword 0 belongs to Head, qword 27 belongs
    /// to Cosplay, and all late slots use the same permutation as SCUnitState.
    /// </summary>
    public static int ToEquipmentIdsIndex(int serverSlot)
    {
        return ToWireSlot(serverSlot);
    }

    public static ulong[] BuildEquipmentIds(IReadOnlyList<Item> serverItems)
    {
        var result = new ulong[SlotCount];
        if (serverItems == null)
            return result;

        var count = Math.Min(serverItems.Count, SlotCount);
        for (var serverSlot = 0; serverSlot < count; serverSlot++)
        {
            var item = serverItems[serverSlot];
            if (item == null)
                continue;

            var idsIndex = ToEquipmentIdsIndex(serverSlot);
            if (idsIndex >= 0)
                result[idsIndex] = item.Id;
        }

        return result;
    }

    public static Item[] ToWireItems(IReadOnlyList<Item> serverItems)
    {
        var wireItems = new Item[SlotCount];
        if (serverItems == null)
            return wireItems;

        var count = Math.Min(serverItems.Count, SlotCount);
        for (var serverSlot = 0; serverSlot < count; serverSlot++)
        {
            var item = serverItems[serverSlot];
            if (item == null)
                continue;

            var wireSlot = ToWireSlot(serverSlot);
            if (wireItems[wireSlot] != null)
                throw new InvalidOperationException($"Equipment wire slot collision: server slots {wireItems[wireSlot].Slot} and {serverSlot} both map to {wireSlot}");
            wireItems[wireSlot] = item;
        }

        return wireItems;
    }

    public static ulong BuildValidMask(IReadOnlyList<Item> serverItems)
    {
        var wireItems = ToWireItems(serverItems);
        ulong mask = 0;
        for (var wireSlot = 0; wireSlot < wireItems.Length; wireSlot++)
        {
            if (wireItems[wireSlot] != null)
                mask |= 1UL << wireSlot;
        }
        return mask;
    }

    private static byte[] BuildInverse()
    {
        var inverse = new byte[SlotCount];
        var seen = new bool[SlotCount];
        for (var serverSlot = 0; serverSlot < ServerToWire.Length; serverSlot++)
        {
            var wireSlot = ServerToWire[serverSlot];
            if (wireSlot >= SlotCount || seen[wireSlot])
                throw new InvalidOperationException("Invalid 1.8.1.0 equipment slot permutation");
            inverse[wireSlot] = checked((byte)serverSlot);
            seen[wireSlot] = true;
        }
        return inverse;
    }
}
