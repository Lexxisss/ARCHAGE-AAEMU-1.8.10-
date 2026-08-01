using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBindSlavePacket : GamePacket
{
    public CSBindSlavePacket() : base(CSOffsets.CSBindSlavePacket, 5)
    {
    }

    /// <summary>
    /// Binds a ship or land vehicle through a skill.
    /// </summary>
    /// <remarks>
    /// Payload is <c>tl:u16, skillType:u32</c>. The skill type was not being read; it is kept
    /// for the log for now, since the bind path does not branch on it yet.
    /// </remarks>
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16();
        var skillType = stream.LeftBytes >= sizeof(uint) ? stream.ReadUInt32() : 0u;

        Logger.Debug("BindSlave, Tl: {0}, SkillType: {1}", tlId, skillType);
        SlaveManager.Instance.BindSlave(Connection, tlId);
    }
}
