using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// Driver input for a ship. Target 1.8.1.0 serializes throttle first and steering second.
/// </summary>
public class ShipRequestMoveType : MoveType
{
    public sbyte Throttle { get; set; }
    public sbyte Steering { get; set; }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        Throttle = stream.ReadSByte(); // shipRequest.throttle
        Steering = stream.ReadSByte(); // shipRequest.steering
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Throttle);
        stream.Write(Steering);
        return stream;
    }
}
