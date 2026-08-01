using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Request to summon a ship or land vehicle.
/// </summary>
/// <remarks>
/// Wire order:
///
///     slaveType:u32, x:i64, y:i64, z:f32, zRot:f32, item:u64,
///     [sourceSlot.type:u8, sourceSlot.index:u8]  - only when item != 0,
///     hideSpawnEffect:bool
///
/// The slot pair is conditional. Reading it unconditionally meant that a request carrying no
/// source item consumed two bytes that were not there, so the spawn-effect flag came off the
/// wrong byte and the read ran past the end of the body.
///
/// The client does not send the persistent record id here; it learns that from the state
/// messages the server sends back.
/// </remarks>
public class CSSpawnSlavePacket : GamePacket
{
    public CSSpawnSlavePacket() : base(CSOffsets.CSSpawnSlavePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var slaveType = stream.ReadUInt32();
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var zRot = stream.ReadSingle();
        var itemId = stream.ReadUInt64();

        var slotType = SlotType.None;
        byte slot = 0;
        if (itemId != 0)
        {
            slotType = (SlotType)stream.ReadByte();
            slot = stream.ReadByte();
        }

        var hideSpawnEffect = stream.LeftBytes > 0 && stream.ReadBoolean();

        Logger.Debug("SpawnSlave, Type: {0}, Item: {1}, Slot: {2}/{3}, Pos: {4},{5},{6}, HideEffect: {7}",
            slaveType, itemId, slotType, slot, x, y, z, hideSpawnEffect);

        var owner = Connection?.ActiveChar;
        if (owner == null)
            return;

        var item = itemId != 0 ? owner.Inventory.GetItemById(itemId) : null;
        if (itemId != 0 && item == null)
        {
            Logger.Warn("SpawnSlave: {0} asked to summon with item {1}, which they do not own", owner.Name, itemId);
            owner.SendErrorMessage(ErrorMessageType.SlaveCannotSpawn);
            return;
        }

        // The request carries where the player wants it, so honour that rather than dropping
        // the vehicle on top of them.
        using var spawnPos = new Transform(null);
        spawnPos.Local.SetPosition(x, y, z);
        spawnPos.Local.SetZRotation(zRot);

        // This handler used to parse the request and do nothing at all with it - the vehicle
        // was never created, which is indistinguishable from the packet never arriving.
        SlaveManager.Instance.Create(owner, null, slaveType, item, spawnPos);
    }
}
