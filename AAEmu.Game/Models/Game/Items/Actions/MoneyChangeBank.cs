using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class MoneyChangeBank : ItemTask
{
    private readonly long _amount;

    public MoneyChangeBank(long amount)
    {
        _type = ItemAction.ChangeBankMoneyAmount; // 2
        _amount = amount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_amount); // i64 in target 1.8.1.0 serializer
        return stream;
    }
}
