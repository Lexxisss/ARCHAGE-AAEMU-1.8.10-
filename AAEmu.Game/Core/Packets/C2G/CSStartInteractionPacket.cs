using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartInteractionPacket : GamePacket
{
    public CSStartInteractionPacket() : base(CSOffsets.CSStartInteractionPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Target 10.8 serializer: BC, BC, UInt32, UInt32, UInt8, UInt32.
        var targetObjId = stream.ReadBc();
        var sourceObjId = stream.ReadBc();
        var extraInfo = stream.ReadUInt32();
        var pickId = stream.ReadUInt32();
        var mouseButton = stream.ReadByte();
        var modifierKeys = stream.ReadUInt32();

        Logger.Info(
            "StartInteraction 10.8: target={0}, source={1}, extraInfo={2}, pickId={3}, mouse={4}, modifiers={5}",
            targetObjId,
            sourceObjId,
            extraInfo,
            pickId,
            mouseButton,
            modifierKeys);

        // Doodads use SC_WORLD_INTERACTION_SKILL_LIST, not the NPC list.
        // The first object is the target in the target DLL. The fallback makes
        // the handler tolerant of older callers that supplied them reversed.
        var doodad = WorldManager.Instance.GetDoodad(targetObjId);
        if (doodad == null)
        {
            doodad = WorldManager.Instance.GetDoodad(sourceObjId);
            if (doodad != null)
                (targetObjId, sourceObjId) = (sourceObjId, targetObjId);
        }

        if (doodad != null)
        {
            Connection.ActiveChar.Quests.LogDoodadQuestMarkerCandidates(doodad);
            var interactions = DoodadManager.Instance.GetInteractionSkills(doodad.FuncGroupId);
            if (interactions.Length == 0)
            {
                Logger.Warn(
                    "Doodad interaction group has no functions: objId={0}, templateId={1}, funcGroupId={2}",
                    doodad.ObjId,
                    doodad.TemplateId,
                    doodad.FuncGroupId);

                Connection.ActiveChar.SendPacket(new SCCancelWorldInteractionPacket(sourceObjId, targetObjId));
                Connection.ClearDoodadInteraction();
                return;
            }

            if (sourceObjId == 0)
                sourceObjId = Connection.ActiveChar.ObjId;

            Connection.ClearDoodadInteraction();
            Connection.ActiveDoodadInteractionTargetObjId = targetObjId;
            Connection.ActiveDoodadInteractionSourceObjId = sourceObjId;
            Connection.ActiveDoodadInteractionExtraInfo = extraInfo;
            Connection.ActiveDoodadInteractionPickId = pickId;
            Connection.ActiveDoodadInteractionMouseButton = mouseButton;
            Connection.ActiveDoodadInteractionModifierKeys = modifierKeys;
            foreach (var interaction in interactions)
                Connection.ActiveDoodadInteractionSkills.Add(interaction);

            Connection.ActiveChar.SendPacket(new SCWorldInteractionSkillListPacket(
                targetObjId,
                sourceObjId,
                extraInfo,
                pickId,
                mouseButton,
                modifierKeys,
                interactions));

            Logger.Info(
                "Doodad interaction list: objId={0}, templateId={1}, funcGroupId={2}, interactions=[{3}]",
                doodad.ObjId,
                doodad.TemplateId,
                doodad.FuncGroupId,
                string.Join(",", interactions));
            return;
        }

        var npc = WorldManager.Instance.GetNpc(targetObjId);
        if (npc != null)
        {
            uint option = 0;
            if (npc.Template.Banker)
                option = SkillsEnum.UseWarehouse;
            else if (npc.Template.AbilityChanger)
                option = SkillsEnum.ChangeSkillsets;
            else if (npc.Template.Auctioneer)
                option = SkillsEnum.UseAuctioneer;
            else if (npc.Template.Priest)
                option = SkillsEnum.Blessing;
            else if (npc.Template.Repairman)
                option = SkillsEnum.Repair;
            else if (npc.Template.Merchant)
                option = SkillsEnum.UseStore;
            else if (npc.Template.Stabler)
                option = SkillsEnum.HealPetSWounds;
            else if (npc.Template.Expedition)
                option = SkillsEnum.FormGuild;
            else if (npc.Template.RecrutingBattlefieldId > 0)
                option = SkillsEnum.WarSupport;
            else if (npc.Template.Blacksmith)
                option = SkillsEnum.ItemFusion;

            Connection.ActiveChar.SendPacket(new SCNpcInteractionSkillListPacket(
                targetObjId,
                sourceObjId,
                unchecked((int)extraInfo),
                unchecked((int)pickId),
                mouseButton,
                unchecked((int)modifierKeys),
                new uint[] { option }));
            return;
        }

        var unit = WorldManager.Instance.GetUnit(targetObjId);

        // A building under construction is not a doodad and has no func group: what it offers is
        // the contribution skill its current stage asks for. Without this the player could reach
        // a foundation but was never offered anything to do with it, so nothing could ever be
        // built. A finished building offers nothing here - what it has instead is its own doors,
        // chests and name plaque, which are doodads and take the path above.
        if (unit is House house)
        {
            Connection.ClearDoodadInteraction();

            var buildSkillId = house.ActiveBuildSkillId;
            if (buildSkillId == 0)
            {
                // A finished building has nothing to contribute to, and that is not a refusal - it
                // is simply a building whose doors, chests and name plaque are what a player deals
                // with, and those are ordinary objects reached the ordinary way.
                //
                // Cancelling here tore the interaction down instead, and the client's own building
                // window went with it: designs built in several stages behaved, because there was
                // always a stage to offer, while a design placed finished in one go answered every
                // approach with a cancel and could never be looked at at all.
                Logger.Info(
                    "House interaction: nothing to contribute, objId={0}, design={1}, step={2}",
                    house.ObjId,
                    house.TemplateId,
                    house.CurrentStep);

                return;
            }

            if (sourceObjId == 0)
                sourceObjId = Connection.ActiveChar.ObjId;

            Logger.Info(
                "House interaction: objId={0}, design={1}, step={2}, action={3}/{4}, skill={5}",
                house.ObjId,
                house.TemplateId,
                house.CurrentStep,
                house.CurrentAction,
                house.AllAction,
                buildSkillId);

            Connection.ActiveChar.SendPacket(new SCWorldInteractionSkillListPacket(
                targetObjId,
                sourceObjId,
                extraInfo,
                pickId,
                mouseButton,
                modifierKeys,
                new[] { buildSkillId }));
            return;
        }

        if (unit is Mate)
        {
            Connection.ActiveChar.SendPacket(new SCNpcInteractionSkillListPacket(
                targetObjId,
                sourceObjId,
                unchecked((int)extraInfo),
                unchecked((int)pickId),
                mouseButton,
                unchecked((int)modifierKeys),
                new uint[] { SkillsEnum.SlaveMounting }));
        }
    }
}
