using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateSkillControllerPacket : GamePacket
{
    public CSCreateSkillControllerPacket() : base(CSOffsets.CSCreateSkillControllerPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        var fallDamageImmune = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // CSCreateSkillController is a notification about the local player's own
        // controller. Never let a client confirm a controller for another unit.
        if (objId != character.ObjId)
        {
            Logger.Warn(
                "Rejected CreateSkillController for foreign unit: character={0}, packetObjId={1}, scType={2}",
                character.ObjId,
                objId,
                scType);
            return;
        }

        var controller = character.ActiveSkillController;
        if (controller == null || !controller.ConfirmClientController(scType, fallDamageImmune))
        {
            Logger.Debug(
                "CreateSkillController has no matching active controller: owner={0}, scType={1}, active={2}",
                objId,
                scType,
                controller?.GetType().Name ?? "<none>");
            return;
        }

        Logger.Debug(
            "Client skill controller confirmed: owner={0}, template={1}, scType={2}, fallDamageImmune={3}",
            objId,
            controller.Template?.Id ?? 0,
            scType,
            fallDamageImmune);
    }
}
