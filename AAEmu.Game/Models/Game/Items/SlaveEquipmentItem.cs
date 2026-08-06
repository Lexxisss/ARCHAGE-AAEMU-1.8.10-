using System;
using System.Buffers.Binary;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Item installed into a ship or land-vehicle equipment slot.
/// </summary>
/// <remarks>
/// Target x2game.dll uses item-detail discriminator 10 for this class and reads a fixed
/// 12-byte payload after the discriminator. The visual component itself is selected from
/// the client database by item template id: item_slave_equipments may point at either a
/// doodad_id or a slave_id.
/// </remarks>
public class SlaveEquipmentItem : Item
{
    private const uint HealthyCurrentState = 1;
    private const uint HealthyMaximumState = 1;

    public override ItemDetailType DetailType => ItemDetailType.SlaveEquipment;
    public override uint DetailBytesLength => 12;

    /// <summary>
    /// True when an old empty/zero detail row was upgraded while loading from MySQL.
    /// ItemManager keeps such an item dirty after inserting it into its container so the
    /// corrected 12-byte state is written back on the next save.
    /// </summary>
    public bool DetailWasMigrated { get; private set; }

    public SlaveEquipmentItem()
    {
        Detail = CreateHealthyDetail();
    }

    public SlaveEquipmentItem(ulong id, ItemTemplate template, int count)
        : base(id, template, count)
    {
        Detail = CreateHealthyDetail();
    }

    public override void ReadDetails(PacketStream stream)
    {
        // Old server builds persisted these items as generic Item rows with an empty detail blob.
        // Upgrade them safely: preserve a complete payload when it exists, otherwise initialise
        // the exact target width instead of reading past the stored blob.
        var loaded = stream.LeftBytes >= DetailBytesLength
            ? stream.ReadBytes((int)DetailBytesLength)
            : Array.Empty<byte>();

        if (loaded.Length == (int)DetailBytesLength && !IsAllZero(loaded))
        {
            Detail = loaded;
            DetailWasMigrated = false;
            return;
        }

        // Target x2game.dll reads detail type 10 as a fixed 12-byte payload (three dwords).
        // A completely zero payload is interpreted as unavailable/broken equipment. The exact
        // higher-level names of all three dwords are not exposed by the stripped target binary,
        // but the first two participate as a current/maximum pair. 1/1 is the smallest safe,
        // lossless "healthy" state and avoids inventing a client-specific durability scale.
        Detail = CreateHealthyDetail();
        DetailWasMigrated = true;
    }

    public override void WriteDetails(PacketStream stream)
    {
        if (Detail == null || Detail.Length != (int)DetailBytesLength || IsAllZero(Detail))
            Detail = CreateHealthyDetail();

        stream.Write(Detail);
    }

    private static byte[] CreateHealthyDetail()
    {
        var result = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), HealthyCurrentState);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), HealthyMaximumState);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), 0u);
        return result;
    }

    private static bool IsAllZero(byte[] value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != 0)
                return false;
        }

        return true;
    }
}
