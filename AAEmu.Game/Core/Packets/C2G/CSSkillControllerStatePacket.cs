using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSkillControllerStatePacket : GamePacket
{
    public CSSkillControllerStatePacket() : base(CSOffsets.CSSkillControllerStatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        var len = 0f;
        var teared = false;
        var cutouted = false;
        if (scType == 0)
        {
            len = stream.ReadSingle();
            teared = stream.ReadBoolean();
            cutouted = stream.ReadBoolean();
        }

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (objId != character.ObjId)
        {
            Logger.Warn(
                "Rejected SkillControllerState for foreign unit: character={0}, packetObjId={1}, scType={2}",
                character.ObjId,
                objId,
                scType);
            return;
        }

        if (character.ActiveSkillController == null)
        {
            Logger.Debug(
                "Ignored SkillControllerState without an active controller: owner={0}, scType={1}",
                objId,
                scType);
            return;
        }

        // The target client handles SC 0x1C1 by finding the unit's already active
        // controller and forwarding this state to it. It does not create a controller.
        // Relay the owner's state to observers; the originating client already applied it.
        character.BroadcastPacket(
            new SCSkillControllerStatePacket(objId, scType, len, teared, cutouted),
            false);

        Logger.Debug(
            "SkillControllerState relayed: owner={0}, scType={1}, len={2}, teared={3}, cutouted={4}",
            objId,
            scType,
            len,
            teared,
            cutouted);
    }
}
