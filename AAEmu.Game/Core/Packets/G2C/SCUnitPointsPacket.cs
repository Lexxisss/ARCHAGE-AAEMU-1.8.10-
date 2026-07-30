using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitPointsPacket : GamePacket
{
    private readonly uint _id;
    private readonly long _preciseHealth;
    private readonly long _preciseMana;

    public SCUnitPointsPacket(uint id, long health, long mana, int highAbilityRsc) : base(SCOffsets.SCUnitPointsPacket, 5)
    {
        _id = id;
        _preciseHealth = health * 100;
        _preciseMana = mana * 100;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_id);
        stream.Write(_preciseHealth);
        stream.Write(_preciseMana);
        return stream;
    }
}
