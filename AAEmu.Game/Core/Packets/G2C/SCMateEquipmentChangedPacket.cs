using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Authoritative result of an equipment change on a mate.
/// </summary>
/// <remarks>
/// The same shape as the slave message: the equipment set the request carries, capped at two
/// records here rather than three, followed by a success flag. The flag is not decorative - the
/// handler reconciles the client's model against it, so a refused change has to be reported
/// rather than left silent.
/// </remarks>
public class SCMateEquipmentChangedPacket : GamePacket
{
    private readonly MateEquipment _mateEquipment;
    private readonly bool _success;

    public SCMateEquipmentChangedPacket(MateEquipment mateEquipment, bool success)
        : base(SCOffsets.SCMateEquipmentChangedPacket, 5)
    {
        _mateEquipment = mateEquipment;
        _success = success;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_mateEquipment); // changes : the same set the request carries, max 2 records
        stream.Write(_success);       // success : bool
        return stream;
    }
}
