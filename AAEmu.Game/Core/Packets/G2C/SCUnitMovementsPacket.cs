using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitMovementsPacket : GamePacket // TODO ... SCOneUnitMovementPacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private (uint id, MoveType type)[] _movements;

    public SCUnitMovementsPacket((uint id, MoveType type)[] movements) : base(SCOffsets.SCUnitMovementsPacket, 1)
    {
        _movements = movements;
    }

    /// <summary>
    /// The client reads no more than this many entries, and the count is what drives its loop.
    /// </summary>
    public const int MaxMovements = 350;

    public override PacketStream Write(PacketStream stream)
    {
        // Announcing more than the client reads would leave it treating the rest of the message
        // as another entry. The overflow is dropped rather than truncated silently.
        var count = Math.Min(_movements.Length, MaxMovements);
        if (_movements.Length > MaxMovements)
            Logger.Warn($"SCUnitMovements: {_movements.Length} entries, sending the first {MaxMovements}");

        stream.Write((ushort)count);
        for (var i = 0; i < count; i++)
        {
            var (id, type) = _movements[i];
            stream.WriteBc(id);
            stream.Write((byte)type.Type);
            stream.Write(type);
        }

        return stream;
    }
}
