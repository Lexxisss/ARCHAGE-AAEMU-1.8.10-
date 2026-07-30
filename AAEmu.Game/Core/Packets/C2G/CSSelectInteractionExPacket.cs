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
/// Exact target serializer 0x399D12B0 contains two BC/object-id fields only.
/// </summary>
public class CSSelectInteractionExPacket : GamePacket
{
    public CSSelectInteractionExPacket() : base(CSOffsets.CSSelectInteractionExPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var firstId = stream.ReadBc();
        var secondId = stream.ReadBc();

        var targetObjId = firstId;
        var interactionId = secondId;

        // The target call-site associates the packet with the active world
        // interaction. Keep a tolerant swap only for captures/builds where the
        // two semantic arguments were forwarded in reverse order.
        if (Connection.ActiveDoodadInteractionTargetObjId != 0 &&
            targetObjId != Connection.ActiveDoodadInteractionTargetObjId &&
            secondId == Connection.ActiveDoodadInteractionTargetObjId)
        {
            targetObjId = secondId;
            interactionId = firstId;
        }

        if (targetObjId == 0)
            targetObjId = Connection.ActiveDoodadInteractionTargetObjId;

        var doodad = WorldManager.Instance.GetDoodad(targetObjId);
        if (doodad == null || Connection.ActiveChar == null)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: missing doodad, first={0}, second={1}, sessionTarget={2}",
                firstId,
                secondId,
                Connection.ActiveDoodadInteractionTargetObjId);
            CancelInteraction(targetObjId);
            return;
        }

        if (Connection.ActiveDoodadInteractionTargetObjId != doodad.ObjId)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: target is outside active session, target={0}, sessionTarget={1}",
                doodad.ObjId,
                Connection.ActiveDoodadInteractionTargetObjId);
            CancelInteraction(doodad.ObjId);
            return;
        }

        var distance = MathUtil.CalculateDistance(
            Connection.ActiveChar.Transform.World.Position,
            doodad.Transform.World.Position);
        if (distance > 6f)
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: doodad too far, objId={0}, distance={1:F2}",
                doodad.ObjId,
                distance);
            CancelInteraction(doodad.ObjId);
            return;
        }

        // The selected action must have been advertised by the current func
        // group loaded from Data/base.sqlite3. Zero is allowed because several
        // client-side world interactions use zero as the action value.
        if (!Connection.ActiveDoodadInteractionSkills.Contains(interactionId))
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: action not in session, objId={0}, interaction={1}, allowed=[{2}]",
                doodad.ObjId,
                interactionId,
                string.Join(",", Connection.ActiveDoodadInteractionSkills));
            CancelInteraction(doodad.ObjId);
            return;
        }

        // Revalidate against the doodad's current phase. A phase may have
        // changed after SC_WORLD_INTERACTION_SKILL_LIST was sent.
        var currentSkills = DoodadManager.Instance.GetInteractionSkills(doodad.FuncGroupId);
        if (!currentSkills.Contains(interactionId))
        {
            Logger.Warn(
                "SelectInteractionEx 10.8: action no longer belongs to current func group, objId={0}, group={1}, interaction={2}",
                doodad.ObjId,
                doodad.FuncGroupId,
                interactionId);
            CancelInteraction(doodad.ObjId);
            return;
        }

        Logger.Info(
            "SelectInteractionEx 10.8: character={0}, doodad={1}, template={2}, group={3}, interaction={4}",
            Connection.ActiveChar.Id,
            doodad.ObjId,
            doodad.TemplateId,
            doodad.FuncGroupId,
            interactionId);

        // This is the existing server-side doodad function chain. It resolves
        // actual_func_type/actual_func_id/next_phase from base.sqlite3.
        doodad.Use(Connection.ActiveChar, interactionId);
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
