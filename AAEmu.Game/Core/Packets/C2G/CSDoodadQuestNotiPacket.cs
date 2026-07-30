using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 10.8 CSDoodadQuestNoti: UInt24 doodad object id + UInt32 quest context id.
/// Sent when the player clicks the quest notifier rendered above a doodad.
/// </summary>
public sealed class CSDoodadQuestNotiPacket : GamePacket
{
    public CSDoodadQuestNotiPacket() : base(CSOffsets.CSDoodadQuestNotiPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var doodadObjId = stream.ReadBc();
        var questId = stream.ReadUInt32();
        var character = Connection.ActiveChar;
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);

        if (doodad == null)
        {
            Logger.Warn("DoodadQuestNoti: missing doodad objId={0}, quest={1}", doodadObjId, questId);
            character.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.InvalidDoodad));
            return;
        }

        if (MathUtil.CalculateDistance(doodad, character) > 12.0f)
        {
            Logger.Warn("DoodadQuestNoti: too far doodad={0}:{1}, quest={2}", doodad.TemplateId, doodad.ObjId, questId);
            character.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.TooFarAwayToInteractWith));
            return;
        }

        var questFunc = doodad.CurrentFuncs
            .Select(func => new
            {
                Func = func,
                Template = DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) as DoodadFuncQuest
            })
            .FirstOrDefault(x => x.Template?.QuestId == questId);

        if (questFunc == null)
        {
            Logger.Warn("DoodadQuestNoti: quest function not found doodad={0}:{1}, group={2}, quest={3}",
                doodad.TemplateId, doodad.ObjId, doodad.FuncGroupId, questId);
            character.SendPacket(new SCQuestContextFailedPacket(questId, QuestStatusFailed.InvalidDoodad));
            return;
        }

        Logger.Debug("DoodadQuestNoti: doodad={0}:{1}, group={2}, quest={3}, kind={4}",
            doodad.TemplateId, doodad.ObjId, doodad.FuncGroupId, questId, questFunc.Template.QuestKindId);
        questFunc.Func.Use(character, doodad, questFunc.Func.SkillId, questFunc.Func.NextPhase);
    }
}
