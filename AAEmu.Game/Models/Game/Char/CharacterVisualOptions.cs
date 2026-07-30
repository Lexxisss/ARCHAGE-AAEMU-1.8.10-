using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterVisualOptions : PacketMarshaler
{
    private byte _flag;
    public byte[] Stp;
    public bool Helmet;
    public bool BackHoldable;
    public bool Cosplay;
    public bool CosplayBackpack;
    public bool Weapon;
    public bool RangedWeapon;

    public CharacterVisualOptions()
    {
        // Target 10.8 uses all seven option bits. These session defaults are
        // replaced with the client's values when CSSpawnCharacter is read.
        // Keeping a complete object is also required for the pre-spawn 0x214.
        _flag = 0x7F;
        Stp = new byte[] { 30, 60, 50, 0, 40, 100 };
        Helmet = true;
        BackHoldable = true;
        Cosplay = true;
        CosplayBackpack = false;
        Weapon = false;
        RangedWeapon = true;
    }

    public override void Read(PacketStream stream)
    {
        _flag = stream.ReadByte();
        if ((_flag & 1) == 1)
            Stp = stream.ReadBytes(6);
        if ((_flag & 2) == 2)
            Helmet = stream.ReadBoolean();
        if ((_flag & 4) == 4)
            BackHoldable = stream.ReadBoolean();
        if ((_flag & 8) == 8)
            Cosplay = stream.ReadBoolean();
        if ((_flag & 16) == 16)
            CosplayBackpack = stream.ReadBoolean();
        if ((_flag & 32) == 32)
            Weapon = stream.ReadBoolean();
        if ((_flag & 64) == 64)
            RangedWeapon = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_flag);
        return Write(stream, _flag);
    }

    public PacketStream Write(PacketStream stream, byte flag)
    {
        if ((flag & 1) == 1)
            stream.Write(Stp);
        if ((flag & 2) == 2)
            stream.Write(Helmet);
        if ((flag & 4) == 4)
            stream.Write(BackHoldable);
        if ((flag & 8) == 8)
            stream.Write(Cosplay);
        if ((flag & 16) == 16)
            stream.Write(CosplayBackpack);
        if ((flag & 32) == 32)
            stream.Write(Weapon);
        if ((flag & 64) == 64)
            stream.Write(RangedWeapon);
        return stream;
    }
    public PacketStream WriteOptions(PacketStream stream)
    {
        // all this data must be output to the SCUnitStatePacket
        stream.Write(Stp);             // stp
        stream.Write(Helmet);          // helmet
        stream.Write(BackHoldable);    // back_holdable
        stream.Write(Cosplay);         // cosplay
        stream.Write(CosplayBackpack); // cosplay_backpack
        stream.Write(Weapon);          // weapon
        stream.Write(RangedWeapon);    // ranged weapon

        return stream;
    }
}
