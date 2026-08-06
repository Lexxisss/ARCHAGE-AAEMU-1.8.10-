using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 10.8 CS_SELECT_INTERACTION_EX (0x0013).
/// </summary>
public class CSSelectInteractionExPacket : GamePacket
{
    public CSSelectInteractionExPacket() : base(CSOffsets.CSSelectInteractionExPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Target capture: source unit BC, target doodad BC, followed by seven option bytes.
        // The previous reader treated the source unit as the doodad and therefore cancelled
        // ladder/helm interactions before the selected function could run.
        var sourceObjId = stream.ReadBc();
        var targetObjId = stream.ReadBc();
        var tail = stream.LeftBytes > 0 ? stream.ReadBytes(stream.LeftBytes) : System.Array.Empty<byte>();

        var character = Connection.ActiveChar;
        var doodad = WorldManager.Instance.GetDoodad(targetObjId);
        if (character == null || doodad == null)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: missing target, source={0}, target={1}, tail={2}",
                sourceObjId,
                targetObjId,
                System.BitConverter.ToString(tail));
            CancelInteraction(targetObjId);
            return;
        }

        if (sourceObjId != 0 && sourceObjId != character.ObjId)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: source mismatch, source={0}, character={1}, target={2}",
                sourceObjId,
                character.ObjId,
                targetObjId);
            CancelInteraction(targetObjId);
            return;
        }

        var distance = MathUtil.CalculateDistance(
            character.Transform.World.Position,
            doodad.Transform.World.Position);
        if (distance > 8f)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: doodad too far, objId={0}, distance={1:F2}",
                doodad.ObjId,
                distance);
            CancelInteraction(doodad.ObjId);
            return;
        }

        var currentSkills = DoodadManager.Instance.GetInteractionSkills(doodad.FuncGroupId);
        uint interactionId = 0;

        // When CSStartInteraction preceded this packet, preserve the action selected from the
        // exact list sent to the client. The seven-byte tail is zero for direct ladder/seat use.
        if (Connection.ActiveDoodadInteractionTargetObjId == doodad.ObjId &&
            Connection.ActiveDoodadInteractionSkills.Count == 1)
        {
            interactionId = Connection.ActiveDoodadInteractionSkills.First();
        }
        else if (currentSkills.Length == 1)
        {
            interactionId = currentSkills[0];
        }
        else if (tail.Length >= 4)
        {
            interactionId = System.BitConverter.ToUInt32(tail, 0);
        }

        if (interactionId != 0 && !currentSkills.Contains(interactionId))
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: invalid action, target={0}, interaction={1}, allowed=[{2}], tail={3}",
                doodad.ObjId,
                interactionId,
                string.Join(",", currentSkills),
                System.BitConverter.ToString(tail));
            CancelInteraction(doodad.ObjId);
            return;
        }

        Logger.Info(
            "SelectInteractionEx 10.8: character={0}, target={1}, template={2}, group={3}, interaction={4}, tail={5}",
            character.Id,
            doodad.ObjId,
            doodad.TemplateId,
            doodad.FuncGroupId,
            interactionId,
            System.BitConverter.ToString(tail));

        doodad.Use(character, interactionId);
        CancelInteraction(doodad.ObjId);
    }

    private void CancelInteraction(uint doodadObjId)
    {
        var sourceObjId = Connection.ActiveDoodadInteractionSourceObjId;
        if (sourceObjId == 0)
            sourceObjId = Connection.ActiveChar?.ObjId ?? 0;

        if (Connection.ActiveChar != null)
            Connection.ActiveChar.SendPacket(new SCCancelWorldInteractionPacket(sourceObjId, doodadObjId));

        Connection.ClearDoodadInteraction();
    }
}
