using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_DOODAD_PHASE_CHANGED (0x02E1).
/// Exact target serializer: BC + five UInt32 + Boolean + optional goods block.
/// Field mapping is based on the target labels and create/update correspondence:
/// funcGroupId, data, growing, puzzleGroup, itemId.
/// </summary>
public class SCDoodadPhaseChangedPacket : GamePacket
{
    private readonly Doodad _doodad;

    public SCDoodadPhaseChangedPacket(Doodad doodad)
        : base(SCOffsets.SCDoodadPhaseChangedPacket, 5)
    {
        _doodad = doodad;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodad.ObjId);                    // +0x10
        stream.Write(_doodad.FuncGroupId);                // +0x14
        stream.Write(unchecked((uint)_doodad.Data));      // +0x18
        stream.Write(_doodad.TimeLeft);                   // +0x20, "growing"
        stream.Write(unchecked((uint)_doodad.PuzzleGroup)); // +0x24
        stream.Write(_doodad.ItemTemplateId);             // +0x1C, serialized here

        stream.Write(_doodad.HasTargetGoodsPayload);      // +0x3A, "isGoods"
        if (_doodad.HasTargetGoodsPayload)
        {
            stream.Write(_doodad.FreshnessTime);          // +0x28
            stream.Write(_doodad.CrafterId);              // +0x30
            stream.Write(_doodad.GoodsAux16);             // +0x38
        }

        return stream;
    }
}
