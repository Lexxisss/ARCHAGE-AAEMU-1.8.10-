using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillCasterType : byte
{
    Unit = 0,
    Unk1 = 1, // Doodad
    Item = 2,
    Mount = 3, // TODO mountSkillType
    Doodad = 4 // Gimmick
}

public abstract class SkillCaster : PacketMarshaler
{
    public SkillCasterType Type { get; set; }
    public uint ObjId { get; set; }

    public override void Read(PacketStream stream)
    {
        ObjId = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Type);
        stream.WriteBc(ObjId);
        return stream;
    }

    public static SkillCaster GetByType(SkillCasterType type)
    {
        SkillCaster obj;
        switch (type)
        {
            case SkillCasterType.Unit:
                obj = new SkillCasterUnit();
                break;
            case SkillCasterType.Unk1:
                obj = new SkillCasterUnk1();
                break;
            case SkillCasterType.Item:
                obj = new SkillItem();
                break;
            case SkillCasterType.Mount:
                obj = new SkillCasterMount();
                break;
            case SkillCasterType.Doodad:
                obj = new SkillDoodad();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        obj.Type = type;
        return obj;
    }
}

public class SkillCasterUnit : SkillCaster
{
    public SkillCasterUnit()
    {
    }

    public SkillCasterUnit(uint objId)
    {
        Type = SkillCasterType.Unit;
        ObjId = objId;
    }
}

public class SkillCasterUnk1 : SkillCaster
{
    public SkillCasterUnk1()
    {
    }

    public SkillCasterUnk1(uint objId)
    {
        Type = SkillCasterType.Unk1;
        ObjId = objId;
    }
}

public class SkillItem : SkillCaster
{
    private ulong _itemId;
    public ulong ItemId
    {
        get => _itemId;
        set
        {
            if (_itemId == value)
                return;
            _itemId = value;
            if (_itemId > 0)
            {
                SkillSourceItem = ItemManager.Instance.GetItemByItemId(value);
                ItemTemplateId = SkillSourceItem?.TemplateId ?? 0;
            }
        }
    }

    public uint ItemTemplateId { get; set; }
    public byte Type1 { get; set; }

    /// <summary>
    /// Trailing value of the item caster block. Verified 8 bytes wide against the target
    /// serializer: caster type 2 is bc3 + u64 + u32 + u8 + u64 = 24 bytes. Reading it as 4
    /// left everything after the caster short by four bytes, so the SkillObject header was
    /// taken from the middle of this value - which is what turned using a coin purse into a
    /// bogus type 4 object and a read past the end of the packet.
    /// </summary>
    public ulong Type2 { get; set; }
    public Item SkillSourceItem { get; private set; }

    public SkillItem()
    {
    }

    public SkillItem(uint objId, ulong itemId, uint itemTemplateId)
    {
        Type = SkillCasterType.Item;
        ObjId = objId;
        ItemId = itemId;
        ItemTemplateId = itemTemplateId;
    }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        ItemId = stream.ReadUInt64();
        ItemTemplateId = stream.ReadUInt32();
        Type1 = stream.ReadByte();
        Type2 = stream.ReadUInt64();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(ItemId);
        stream.Write(ItemTemplateId);
        stream.Write(Type1);
        stream.Write(Type2);
        return stream;
    }
}

public class SkillCasterMount : SkillCaster
{
    public uint MountSkillTemplateId { get; set; }

    public SkillCasterMount()
    {
    }

    public SkillCasterMount(uint objId)
    {
        Type = SkillCasterType.Mount;
        ObjId = objId;
    }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        MountSkillTemplateId = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(MountSkillTemplateId);
        return stream;
    }
}

public class SkillDoodad : SkillCaster
{
    public SkillDoodad()
    {
    }

    public SkillDoodad(uint objId)
    {
        Type = SkillCasterType.Doodad;
        ObjId = objId;
    }
}
