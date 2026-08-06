using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Rebinds the 35 target-client equipment slots of an already registered unit
/// to their runtime item identities.
/// </summary>
/// <remarks>
/// Target x2game.dll serializer 0x399D08A0 writes a compressed object id and
/// exactly 0x23 unsigned 64-bit values. There is no count field and no leading
/// ES_INVALID entry. Index 0 is Head and index 27 is Cosplay. The values are
/// runtime item ids, not item template ids and not client asset ids.
/// </remarks>
public sealed class SCUnitEquipmentIdsPacket : GamePacket
{
    public const int SlotCount = 0x23;

    private readonly uint _objectId;
    private readonly ulong[] _equipmentIds;

    public static SCUnitEquipmentIdsPacket FromServerEquipment(uint objectId, System.Collections.Generic.IReadOnlyList<Item> serverItems)
    {
        return new SCUnitEquipmentIdsPacket(
            objectId, Protocol1810EquipmentLayout.BuildEquipmentIds(serverItems));
    }

    public SCUnitEquipmentIdsPacket(uint objectId, ulong[] equipmentIds)
        : base(SCOffsets.SCUnitEquipmentIdsPacket, 5)
    {
        _objectId = objectId;
        _equipmentIds = new ulong[SlotCount];
        if (equipmentIds != null)
            Array.Copy(equipmentIds, _equipmentIds, Math.Min(equipmentIds.Length, SlotCount));
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objectId);
        for (var slot = 0; slot < SlotCount; slot++)
            stream.Write(_equipmentIds[slot]);
        return stream;
    }
}
