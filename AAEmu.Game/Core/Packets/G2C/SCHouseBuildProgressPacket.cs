using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// How far along a building is, and what it should look like while it gets there.
/// </summary>
/// <remarks>
/// Fourteen bytes. The handle leads at two - it was going out as four, which pushed the three
/// fields behind it along by two bytes each and left the client reading the model out of the
/// middle of two numbers.
///
/// The client finds the building by the handle, compares the model with the one it is currently
/// drawing, and swaps the model when they differ; the two step counts it just stores. So the model
/// is the one belonging to the stage the building is at, not the finished design's.
/// </remarks>
public class SCHouseBuildProgressPacket : GamePacket
{
    private readonly ushort _tl;
    private readonly uint _modelId;
    private readonly int _allStep;
    private readonly int _curStep;

    public SCHouseBuildProgressPacket(ushort tl, uint modelId, int allStep, int curStep) : base(SCOffsets.SCHouseBuildProgressPacket, 5)
    {
        _tl = tl;
        _modelId = modelId;
        _allStep = allStep;
        _curStep = curStep;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);      // tl      : u16
        stream.Write(_modelId); // modelId : u32
        stream.Write(_allStep); // allstep : i32
        stream.Write(_curStep); // curstep : i32
        return stream;
    }
}
