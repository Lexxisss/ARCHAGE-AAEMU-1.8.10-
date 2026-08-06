using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Logging;
namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeTargetPacket : GamePacket
{
    public CSChangeTargetPacket() : base(CSOffsets.CSChangeTargetPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var targetId = stream.ReadBc();
        if (targetId == 0)
        {
            Connection.ActiveChar.SendMessage("Selected nothing");
            return;
        }
        var target = WorldManager.Instance.GetUnit(targetId);
        if (target == null)
        {
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, TemplateId: not found in Db");
            return;
        }

        Connection
                    .ActiveChar
                    .CurrentTarget = target;

        Connection
            .ActiveChar
            .BroadcastPacket(
                new SCTargetChangedPacket(Connection.ActiveChar.ObjId, targetId), true);

        if (Connection.ActiveChar.CurrentTarget is Portal portal)
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, TemplateId: {portal.TemplateId}\nPos: {portal.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Npc npc)
        {
            // Keep NPC diagnostics in the dedicated file logger. Chat delivery is
            // client-channel/version dependent and was not reliable on 1.8.1.0.
            // Selection is diagnostic only. Never replay NPC equipment or visual
            // options on click: the target client treats those packets as live
            // deltas and may rebuild/hide already attached slots.
            NpcDebugLogger.Snapshot("target-selected", Connection.ActiveChar, npc);
        }
        else if (Connection.ActiveChar.CurrentTarget is House house)
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, HouseId: {house.Id}, Pos: {house.Transform}," +
                                              $" Player: {Connection.ActiveChar.Id}, Pos: {Connection.ActiveChar.Transform}" +
                                              $"\nx: {house.Transform.World.Position.X - Connection.ActiveChar.Transform.World.Position.X}" +
                                              $"y: {house.Transform.World.Position.Y - Connection.ActiveChar.Transform.World.Position.Y}" +
                                              $"z: {house.Transform.World.Position.Z - Connection.ActiveChar.Transform.World.Position.Z}");
        else if (Connection.ActiveChar.CurrentTarget is Transfer transfer)
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, Transfer TemplateId: {transfer.TemplateId}\nPos: {transfer.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Slave slave)
            Connection.ActiveChar.SendMessage($"ObjId: {slave.ObjId}, Slave TemplateId: {slave.TemplateId}, Id: {slave.Id}, Owner: {slave.Summoner?.Name}\nPos: {slave.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Character character)
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, CharacterId: {character.Id}, \nPos: {character.Transform.ToFullString(true, true)}");
        else
            Connection.ActiveChar.SendMessage($"ObjId: {targetId}, Pos: {Connection.ActiveChar.CurrentTarget.Transform}, {Connection.ActiveChar.CurrentTarget.Name}");
    }
}
