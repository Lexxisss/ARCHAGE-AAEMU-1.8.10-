using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    protected ItemAction _type;
    protected ItemTaskLogType _logType = ItemTaskLogType.UpdateOnly;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);     // action
        stream.Write((byte)_logType);  // tLogt
        return stream;
    }
}
