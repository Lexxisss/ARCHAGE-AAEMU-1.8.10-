using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 10.8 CS_CHANGE_DOODAD_PHASE (0x0160).
/// Exact target-family wire size: BC object id + three UInt32 values.
/// The three semantic names are not present in the stripped target DLL, so the
/// server parses them but does not trust the client to mutate authoritative
/// phase state. Real phase changes are performed by Doodad.Use()/doodad funcs.
/// </summary>
public class CSChangeDoodadPhasePacket : GamePacket
{
    public CSChangeDoodadPhasePacket() : base(CSOffsets.CSChangeDoodadPhasePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var value1 = stream.ReadUInt32();
        var value2 = stream.ReadUInt32();
        var value3 = stream.ReadUInt32();
        var doodad = WorldManager.Instance.GetDoodad(objId);

        Logger.Info(
            "ChangeDoodadPhase 10.8: character={0}, objId={1}, template={2}, values={3}/{4}/{5}; authoritative state unchanged",
            Connection.ActiveChar?.Id ?? 0,
            objId,
            doodad?.TemplateId ?? 0,
            value1,
            value2,
            value3);
    }
}
