using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// SkillObject payload selector, verified against the target x2game.dll serializer at
/// 0x399F7C10. The switch has explicit branches for types 1..30; type 0 and 31..63 carry no
/// payload. The previous table was lifted from a 3.0+ client and was shifted by one from
/// type 6 onwards, so every object above 5 was decoded with the wrong layout.
/// </summary>
/// <remarks>
/// Open question, deliberately left as-is: the verification report labels the 8-byte
/// position fields here (types 1, 4 and 7) as <c>double</c>, while we decode them as
/// fixed-point via <see cref="Helpers.ConvertLongX"/> like the rest of this codebase. The
/// width is the same either way so nothing misaligns, but positional skills are reported
/// in-game as not always landing exactly on the aimed point, which is what a wrong
/// fixed-point/IEEE interpretation would look like. Confirm against the serializer before
/// changing - the same call is used by many other packets and they must move together.
/// </remarks>
public enum SkillObjectType
{
    None = 0,
    Portal = 1,
    NamedPortal = 2,
    Message = 3,
    Position = 4,
    Step = 5,
    ItemGradeEnchantingSupport = 6,
    HousingPlacement = 7,
    ItemMaterials = 8,
    ChangeIndex = 9,
    IndexedCount = 10,
    CountAll = 11,
    SelectSlotBit = 12,
    AutoUseAaPoint = 13,
    ValueList = 14,
    SlotIndex = 15,
    Count = 16,
    PackageSeal = 17,
    ByProc = 18,
    Ability = 19,
    Smelting = 20,
    Craft = 21,
    EquipSlot = 22,
    SubtypeLevel = 23,
    Id = 24,
    PageIndex = 25,
    Mapping = 26,
    Color = 27,
    /// <summary>Two generic u32 values; the observed sit / lie down object uses this.</summary>
    Posture = 28,
    CharRace = 29,
    PortalId = 30
}

public class SkillObject : PacketMarshaler
{
    public SkillObjectType Flag { get; set; } = SkillObjectType.None;

    // The target client packs two boolean flags into bits 7 and 6 of the
    // SkillObject header. Preserve them instead of discarding the incoming bits.
    public bool Flag80 { get; set; }
    public bool Flag40 { get; set; }

    /// <summary>
    /// Serialized by the target after the type payload, for every type including None.
    /// Confirmed: the serializer always reads/writes this trailing byte.
    /// </summary>
    public byte InputDirection { get; set; }

    /// <summary>Types 0 and 31..63 hit the default branch and carry no payload.</summary>
    public override void Read(PacketStream stream)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        var header = (byte)((byte)Flag & 0x3F);
        if (Flag40)
            header |= 0x40;
        if (Flag80)
            header |= 0x80;

        stream.Write(header);
        return stream;
    }

    public static SkillObject GetByType(SkillObjectType flag)
    {
        SkillObject obj = flag switch
        {
            SkillObjectType.Portal => new SkillObjectUnk1(),
            SkillObjectType.NamedPortal => new SkillObjectUnk2(),
            SkillObjectType.Message => new SkillObjectMessage(),
            SkillObjectType.Position => new SkillObjectPosition(),
            SkillObjectType.Step => new SkillObjectStep(),
            SkillObjectType.ItemGradeEnchantingSupport => new SkillObjectItemGradeEnchantingSupport(),
            SkillObjectType.HousingPlacement => new SkillObjectHousingPlacement(),
            SkillObjectType.ItemMaterials => new SkillObjectItemMaterials(),
            SkillObjectType.ChangeIndex => new SkillObjectChangeIndex(),
            SkillObjectType.IndexedCount => new SkillObjectIndexedCount(),
            SkillObjectType.CountAll => new SkillObjectCountAll(),
            SkillObjectType.SelectSlotBit => new SkillObjectSelectSlotBit(),
            SkillObjectType.AutoUseAaPoint => new SkillObjectAutoUseAaPoint(),
            SkillObjectType.ValueList => new SkillObjectValueList(),
            SkillObjectType.SlotIndex => new SkillObjectSlotIndex(),
            SkillObjectType.Count => new SkillObjectCount(),
            SkillObjectType.PackageSeal => new SkillObjectPackageSeal(),
            SkillObjectType.ByProc => new SkillObjectByProc(),
            SkillObjectType.Ability => new SkillObjectAbility(),
            SkillObjectType.Smelting => new SkillObjectSmelting(),
            SkillObjectType.Craft => new SkillObjectCraft(),
            SkillObjectType.EquipSlot => new SkillObjectEquipSlot(),
            SkillObjectType.SubtypeLevel => new SkillObjectSubtypeLevel(),
            SkillObjectType.Id => new SkillObjectId(),
            SkillObjectType.PageIndex => new SkillObjectPageIndex(),
            SkillObjectType.Mapping => new SkillObjectMapping(),
            SkillObjectType.Color => new SkillObjectColor(),
            SkillObjectType.Posture => new SkillObjectPosture(),
            SkillObjectType.CharRace => new SkillObjectCharRace(),
            SkillObjectType.PortalId => new SkillObjectPortalId(),
            _ => new SkillObject()
        };

        obj.Flag = flag;
        return obj;
    }
}

/// <summary>Type 1: u8 subtype, u32 id, x, y, float z, u32 indunZoneKey.</summary>
public class SkillObjectUnk1 : SkillObject
{
    public byte Type { get; set; }
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int IndunZoneKey { get; set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadByte();
        Id = stream.ReadInt32();
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
        IndunZoneKey = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Type);
        stream.Write(Id);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        stream.Write(IndunZoneKey);
        return stream;
    }
}

/// <summary>Type 2: u32 id, u16 length + name bytes.</summary>
public class SkillObjectUnk2 : SkillObject
{
    public int Id { get; set; }
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadInt32();
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(Name);
        return stream;
    }
}

/// <summary>Type 3: u16 length + message bytes.</summary>
public class SkillObjectMessage : SkillObject
{
    public string Msg { get; set; }

    public override void Read(PacketStream stream)
    {
        Msg = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Msg);
        return stream;
    }
}

/// <summary>Type 4: x, y, float z. 20 bytes.</summary>
public class SkillObjectPosition : SkillObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        return stream;
    }
}

/// <summary>Type 5: u32 step.</summary>
public class SkillObjectStep : SkillObject
{
    public int Step { get; set; }

    public override void Read(PacketStream stream) => Step = stream.ReadInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Step);
        return stream;
    }
}

/// <summary>Type 6: u64 supportItemId, u8 autoUseAAPoint. Was wrongly mapped to type 7.</summary>
public class SkillObjectItemGradeEnchantingSupport : SkillObject
{
    public ulong SupportItemId { get; set; }
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        SupportItemId = stream.ReadUInt64();
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(SupportItemId);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>Type 7: u32 subtype, x, y, float z, float rot, u32 totalTax.</summary>
public class SkillObjectHousingPlacement : SkillObject
{
    public uint Subtype { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rot { get; set; }
    public uint TotalTax { get; set; }

    public override void Read(PacketStream stream)
    {
        Subtype = stream.ReadUInt32();
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
        Rot = stream.ReadSingle();
        TotalTax = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Subtype);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        stream.Write(Rot);
        stream.Write(TotalTax);
        return stream;
    }
}

/// <summary>Type 8: fixed 48-byte material block, then u8 autoUseAAPoint. No length prefix.</summary>
public class SkillObjectItemMaterials : SkillObject
{
    public const int MaterialBlockSize = 48;

    public byte[] MaterialItemIds { get; set; } = new byte[MaterialBlockSize];
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        MaterialItemIds = stream.ReadBytes(MaterialBlockSize);
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        var block = new byte[MaterialBlockSize];
        MaterialItemIds?.CopyTo(block, 0);
        stream.Write(block);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>Type 9: u32 changeIndex, u32 changeToGroupType.</summary>
public class SkillObjectChangeIndex : SkillObject
{
    public uint ChangeIndex { get; set; }
    public uint ChangeToGroupType { get; set; }

    public override void Read(PacketStream stream)
    {
        ChangeIndex = stream.ReadUInt32();
        ChangeToGroupType = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(ChangeIndex);
        stream.Write(ChangeToGroupType);
        return stream;
    }
}

/// <summary>Type 10: u8 index, u32 count, u8 continuous.</summary>
public class SkillObjectIndexedCount : SkillObject
{
    public byte Index { get; set; }
    public uint Count { get; set; }
    public bool Continuous { get; set; }

    public override void Read(PacketStream stream)
    {
        Index = stream.ReadByte();
        Count = stream.ReadUInt32();
        Continuous = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Index);
        stream.Write(Count);
        stream.Write(Continuous);
        return stream;
    }
}

/// <summary>Type 11: u32 count, u8 isAll.</summary>
public class SkillObjectCountAll : SkillObject
{
    public uint Count { get; set; }
    public bool IsAll { get; set; }

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        IsAll = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Count);
        stream.Write(IsAll);
        return stream;
    }
}

/// <summary>Type 12: u32 selectSlotBit.</summary>
public class SkillObjectSelectSlotBit : SkillObject
{
    public uint SelectSlotBit { get; set; }

    public override void Read(PacketStream stream) => SelectSlotBit = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(SelectSlotBit);
        return stream;
    }
}

/// <summary>Type 13: u8 autoUseAAPoint.</summary>
public class SkillObjectAutoUseAaPoint : SkillObject
{
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream) => AutoUseAaPoint = stream.ReadBoolean();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>Type 14: u32 count then exactly 50 u64 values - the loop ignores count.</summary>
public class SkillObjectValueList : SkillObject
{
    public const int ValueCount = 50;

    public uint Count { get; set; }
    public ulong[] Values { get; set; } = new ulong[ValueCount];

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        Values = new ulong[ValueCount];
        for (var i = 0; i < ValueCount; i++)
            Values[i] = stream.ReadUInt64();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Count);
        for (var i = 0; i < ValueCount; i++)
            stream.Write(Values != null && i < Values.Length ? Values[i] : 0UL);
        return stream;
    }
}

/// <summary>Type 15: u8 slotIndex.</summary>
public class SkillObjectSlotIndex : SkillObject
{
    public byte SlotIndex { get; set; }

    public override void Read(PacketStream stream) => SlotIndex = stream.ReadByte();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(SlotIndex);
        return stream;
    }
}

/// <summary>Type 16: u32 count.</summary>
public class SkillObjectCount : SkillObject
{
    public uint Count { get; set; }

    public override void Read(PacketStream stream) => Count = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Count);
        return stream;
    }
}

/// <summary>Type 17: u8 package, u32 sealCount.</summary>
public class SkillObjectPackageSeal : SkillObject
{
    public byte Package { get; set; }
    public uint SealCount { get; set; }

    public override void Read(PacketStream stream)
    {
        Package = stream.ReadByte();
        SealCount = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Package);
        stream.Write(SealCount);
        return stream;
    }
}

/// <summary>Type 18: u8 byProc.</summary>
public class SkillObjectByProc : SkillObject
{
    public byte ByProc { get; set; }

    public override void Read(PacketStream stream) => ByProc = stream.ReadByte();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(ByProc);
        return stream;
    }
}

/// <summary>Type 19: u32 ability.</summary>
public class SkillObjectAbility : SkillObject
{
    public uint Ability { get; set; }

    public override void Read(PacketStream stream) => Ability = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Ability);
        return stream;
    }
}

/// <summary>Type 20: u8 autoUseAAPoint, u32 smeltingDescId.</summary>
public class SkillObjectSmelting : SkillObject
{
    public bool AutoUseAaPoint { get; set; }
    public uint SmeltingDescId { get; set; }

    public override void Read(PacketStream stream)
    {
        AutoUseAaPoint = stream.ReadBoolean();
        SmeltingDescId = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(AutoUseAaPoint);
        stream.Write(SmeltingDescId);
        return stream;
    }
}

/// <summary>Type 21: u32 craftType, u32 craftCount.</summary>
public class SkillObjectCraft : SkillObject
{
    public uint CraftType { get; set; }
    public uint CraftCount { get; set; }

    public override void Read(PacketStream stream)
    {
        CraftType = stream.ReadUInt32();
        CraftCount = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(CraftType);
        stream.Write(CraftCount);
        return stream;
    }
}

/// <summary>Type 22: u8 subtype, u32 equipSlot, u8 autoUseAAPoint.</summary>
public class SkillObjectEquipSlot : SkillObject
{
    public byte Subtype { get; set; }
    public uint EquipSlot { get; set; }
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        Subtype = stream.ReadByte();
        EquipSlot = stream.ReadUInt32();
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Subtype);
        stream.Write(EquipSlot);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}

/// <summary>Type 23: u8 subtype, u8 level.</summary>
public class SkillObjectSubtypeLevel : SkillObject
{
    public byte Subtype { get; set; }
    public byte Level { get; set; }

    public override void Read(PacketStream stream)
    {
        Subtype = stream.ReadByte();
        Level = stream.ReadByte();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Subtype);
        stream.Write(Level);
        return stream;
    }
}

/// <summary>Type 24: u64 id.</summary>
public class SkillObjectId : SkillObject
{
    public ulong Id { get; set; }

    public override void Read(PacketStream stream) => Id = stream.ReadUInt64();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        return stream;
    }
}

/// <summary>Type 25: u8 pageIndex.</summary>
public class SkillObjectPageIndex : SkillObject
{
    public byte PageIndex { get; set; }

    public override void Read(PacketStream stream) => PageIndex = stream.ReadByte();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(PageIndex);
        return stream;
    }
}

/// <summary>Type 26: u32 mappingId.</summary>
public class SkillObjectMapping : SkillObject
{
    public uint MappingId { get; set; }

    public override void Read(PacketStream stream) => MappingId = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(MappingId);
        return stream;
    }
}

/// <summary>Type 27: u32 color.</summary>
public class SkillObjectColor : SkillObject
{
    public uint Color { get; set; }

    public override void Read(PacketStream stream) => Color = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Color);
        return stream;
    }
}

/// <summary>Type 28: two generic u32 values. Sit / lie down arrives as this type.</summary>
public class SkillObjectPosture : SkillObject
{
    public uint Value0 { get; set; }
    public uint Value1 { get; set; }

    public override void Read(PacketStream stream)
    {
        Value0 = stream.ReadUInt32();
        Value1 = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Value0);
        stream.Write(Value1);
        return stream;
    }
}

/// <summary>Type 29: u8 CharRace.</summary>
public class SkillObjectCharRace : SkillObject
{
    public byte CharRace { get; set; }

    public override void Read(PacketStream stream) => CharRace = stream.ReadByte();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(CharRace);
        return stream;
    }
}

/// <summary>Type 30: u32 portalId, bc3 id.</summary>
public class SkillObjectPortalId : SkillObject
{
    public uint PortalId { get; set; }
    public uint ObjId { get; set; }

    public override void Read(PacketStream stream)
    {
        PortalId = stream.ReadUInt32();
        ObjId = stream.ReadBc();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(PortalId);
        stream.WriteBc(ObjId);
        return stream;
    }
}
