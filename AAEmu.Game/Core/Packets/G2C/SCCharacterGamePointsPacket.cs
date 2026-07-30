using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Character game-point map (0x0228). Target bodies use a one-byte count
/// followed by count entries of byte kind + int32 amount.
/// </summary>
public class SCCharacterGamePointsPacket : GamePacket
{
    private readonly int[,] _points;

    public SCCharacterGamePointsPacket(Character character)
        : this(new[,]
        {
            { 1, character.VocationPoint },
            { 0, character.HonorPoint }
        })
    {
    }

    public SCCharacterGamePointsPacket(int[,] points)
        : base(SCOffsets.SCCharacterGamePointsPacket, 5)
    {
        _points = points;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var rows = _points.GetUpperBound(0) + 1;
        stream.Write((byte)rows);
        for (var i = 0; i < rows; i++)
        {
            stream.Write((byte)_points[i, 0]);
            stream.Write(_points[i, 1]);
        }

        return stream;
    }
}
