using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Authoritative result of an equipment change on a ship or land vehicle.
/// </summary>
/// <remarks>
/// This used to write nothing at all, so installing ship equipment never reached the client.
///
/// The trailing success flag is not decorative - the handler reconciles the client's model
/// against it, so a refused change has to be reported rather than left silent.
/// </remarks>
public class SCSlaveEquipmentChangedPacket : GamePacket
{
    private readonly SlaveEquipment _slaveEquipment;
    private readonly bool _success;

    public SCSlaveEquipmentChangedPacket(SlaveEquipment slaveEquipment, bool success)
        : base(SCOffsets.SCSlaveEquipmentChangedPacket, 5)
    {
        _slaveEquipment = slaveEquipment;
        _success = success;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_slaveEquipment); // changes : the same set the request carries, max 3 records
        stream.Write(_success);        // success : bool
        return stream;
    }
}
