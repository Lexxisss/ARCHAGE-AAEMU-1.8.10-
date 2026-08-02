using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class Item : PacketMarshaler, IComparable<Item>
{
    private byte _worldId;
    private ulong _ownerId;
    private ulong _id;
    private uint _templateId;
    private SlotType _slotType;
    private int _slot;
    private byte _grade;
    private ItemFlag _itemFlags;
    private int _count;
    private int _lifespanMins;
    private uint _madeUnitId;
    private DateTime _createTime;
    private DateTime _unsecureTime;
    private DateTime _unpackTime;
    private uint _imageItemTemplateId;
    private bool _isDirty;
    private ulong _uccId;
    private DateTime _expirationTime;
    private double _expirationOnlineMinutesLeft;
    private DateTime _chargeUseSkillTime;
    private byte _flags;
    private byte _durability;
    private short _chargeCount;
    private ushort _TemperPhysical;
    private ushort _TemperMagical;
    private uint _runeId;
    private DateTime _chargeTime;
    private DateTime _chargeProcTime;
    private byte _mappingFailBonus;
    private byte _elementLevel;

    public bool IsDirty { get => _isDirty; set => _isDirty = value; }
    public byte WorldId { get => _worldId; set { _worldId = value; _isDirty = true; } }
    public ulong OwnerId { get => _ownerId; set { _ownerId = value; _isDirty = true; } }
    public ulong Id { get => _id; set { _id = value; _isDirty = true; } }
    public uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }
    public ItemTemplate Template { get; set; }
    public virtual uint DetailBytesLength { get; } = 0;
    public SlotType SlotType { get => _slotType; set { _slotType = value; _isDirty = true; } }
    public int Slot { get => _slot; set { _slot = value; _isDirty = true; } }
    public byte Grade { get => _grade; set { _grade = value; _isDirty = true; } }
    public ItemFlag ItemFlags { get => _itemFlags; set { _itemFlags = value; _isDirty = true; } }
    public int Count { get => _count; set { _count = value; _isDirty = true; } }
    public int LifespanMins { get => _lifespanMins; set { _lifespanMins = value; _isDirty = true; } }
    public uint MadeUnitId { get => _madeUnitId; set { _madeUnitId = value; _isDirty = true; } }
    public DateTime CreateTime { get => _createTime; set { _createTime = value; _isDirty = true; } }
    public DateTime UnsecureTime { get => _unsecureTime; set { _unsecureTime = value; _isDirty = true; } }
    public DateTime UnpackTime { get => _unpackTime; set { _unpackTime = value; _isDirty = true; } }
    public uint ImageItemTemplateId { get => _imageItemTemplateId; set { _imageItemTemplateId = value; _isDirty = true; } }

    /// <summary>
    /// Internal representation of the exact time a item will expire (UTC)
    /// </summary>
    public DateTime ExpirationTime
    {
        get => _expirationTime;
        set
        {
            if (_expirationTime != value)
            {
                _expirationTime = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Internal representation of the time this item has left before expiring, only counting down if the owning character is online
    /// </summary>
    public double ExpirationOnlineMinutesLeft
    {
        get => _expirationOnlineMinutesLeft;
        set
        {
            _expirationOnlineMinutesLeft = value;
            _isDirty = true;
        }
    }

    public ulong UccId
    {
        get => _uccId;
        set
        {
            _uccId = value;
            if (value > 0)
                SetFlag(ItemFlag.HasUCC);
            else
                RemoveFlag(ItemFlag.HasUCC);
            _isDirty = true;
        }
    }

    public DateTime ChargeStartTime { get; set; } = DateTime.MinValue;
    /// <summary>
    /// Which detail block this item carries. The client accepts 1 through 14 and refuses to
    /// read anything else, so this must never go out as 0 or above 14.
    /// </summary>
    public virtual ItemDetailType DetailType { get; set; }
    public DateTime ChargeUseSkillTime { get => _chargeUseSkillTime; set { _chargeUseSkillTime = value; _isDirty = true; } }
    public byte Flags { get => _flags; set { _flags = value; _isDirty = true; } }
    public byte Durability { get => _durability; set { _durability = value; _isDirty = true; } }
    public short ChargeCount { get => _chargeCount; set { _chargeCount = value; _isDirty = true; } }
    public DateTime ChargeTime { get => _chargeTime; set { _chargeTime = value; _isDirty = true; } }
    public DateTime ChargeProcTime { get => _chargeProcTime; set { _chargeProcTime = value; _isDirty = true; } }
    public byte MappingFailBonus { get => _mappingFailBonus; set { _mappingFailBonus = value; _isDirty = true; } }
    public byte ElementLevel { get => _elementLevel; set { _elementLevel = value; _isDirty = true; } }
    public ushort TemperPhysical { get => _TemperPhysical; set { _TemperPhysical = value; _isDirty = true; } }
    public ushort TemperMagical { get => _TemperMagical; set { _TemperMagical = value; _isDirty = true; } }
    public uint RuneId { get => _runeId; set { _runeId = value; _isDirty = true; } }

    public uint[] GemIds { get; set; }
    public byte[] Detail { get; set; }

    // Helper
    public ItemContainer _holdingContainer { get; set; }

    public static uint Coins { get; } = 500;
    public static uint TaxCertificate { get; } = 31891;
    public static uint BoundTaxCertificate { get; } = 31892;
    public static uint AppraisalCertificate { get; } = 28085;
    public static uint CrestStamp { get; } = 17662;
    public static uint CrestInk { get; } = 17663;
    public static uint SheetMusic { get; } = 28051;
    public static uint SalonCertificate { get; } = 30811;

    /// <summary>
    /// Sort will use itemSlot numbers
    /// </summary>
    /// <param name="otherItem"></param>
    /// <returns></returns>
    public int CompareTo(Item otherItem)
    {
        if (otherItem == null) return 1;
        return this.Slot.CompareTo(otherItem.Slot);
    }

    public Item()
    {
        WorldId = AppConfiguration.Instance.Id;
        OwnerId = 0;
        Slot = -1;
        _holdingContainer = null;
        _isDirty = true;
        GemIds = new uint[18];
    }

    public Item(byte worldId)
    {
        WorldId = worldId;
        OwnerId = 0;
        Slot = -1;
        _holdingContainer = null;
        _isDirty = true;
        GemIds = new uint[18];
    }

    public Item(ulong id, ItemTemplate template, int count)
    {
        WorldId = AppConfiguration.Instance.Id;
        OwnerId = 0;
        Id = id;
        TemplateId = template.Id;
        Template = template;
        Count = count;
        Slot = -1;
        _holdingContainer = null;
        _isDirty = true;
        GemIds = new uint[18];
    }

    public Item(byte worldId, ulong id, ItemTemplate template, int count)
    {
        WorldId = worldId;
        OwnerId = 0;
        Id = id;
        TemplateId = template.Id;
        Template = template;
        Count = count;
        Slot = -1;
        _holdingContainer = null;
        _isDirty = true;
        GemIds = new uint[18];
    }

    public override void Read(PacketStream stream)
    {
        TemplateId = stream.ReadUInt32();
        if (TemplateId == 0)
            return;

        Id = stream.ReadUInt64();
        Grade = stream.ReadByte();
        ItemFlags = (ItemFlag)stream.ReadByte();
        Count = stream.ReadInt32();

        DetailType = (ItemDetailType)stream.ReadByte();
        ReadDetails(stream);

        CreateTime = stream.ReadDateTime();
        LifespanMins = stream.ReadInt32();
        MadeUnitId = checked((uint)stream.ReadUInt64());
        WorldId = stream.ReadByte();
        UnsecureTime = stream.ReadDateTime();
        UnpackTime = stream.ReadDateTime();
        ChargeUseSkillTime = stream.ReadDateTime(); // added in 1.7
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(TemplateId); // type
        if (TemplateId == 0)
            return stream;

        stream.Write(Id);    // id
        stream.Write(Grade); // grade
        stream.Write((byte)ItemFlags); // flags | bounded
        stream.Write(Count); // stackSize

        stream.Write((byte)DetailType); // detailType
        WriteDetails(stream);

        stream.Write(CreateTime);
        stream.Write(LifespanMins);
        stream.Write((ulong)MadeUnitId);
        stream.Write(WorldId);
        stream.Write(UnsecureTime);
        stream.Write(UnpackTime);
        stream.Write(ChargeUseSkillTime); // added in 1.7

        return stream;
    }

    /// <summary>
    /// Bytes the detail block occupies after its discriminator, for every variant the client
    /// carries as a fixed-size payload. Variant 1 is structured and is written out field by
    /// field instead, so it is absent here.
    /// </summary>
    /// <remarks>
    /// These are the client's own sizes. The lengths used to be written as "payload plus one"
    /// and then decremented, which hid two mistakes: the vehicle variant was four bytes short,
    /// and the mount variant was written fourteen bytes shorter than it was read. Both put every
    /// item after them in the same message out of step. Variants twelve through fourteen were
    /// missing entirely and were written as nothing at all.
    ///
    /// Subclasses that decode a variant override the read and write methods and never consult
    /// this table; it is what the rest fall back on, and what keeps an unrecognised variant the
    /// right length instead of silently truncating the message.
    /// </remarks>
    public static int DetailPayloadLength(ItemDetailType detailType)
    {
        return detailType switch
        {
            ItemDetailType.Slave => 33,
            ItemDetailType.Mate => 20,
            ItemDetailType.Ucc => 9,
            ItemDetailType.Treasure => 24,
            ItemDetailType.BigFish => 16,
            ItemDetailType.Decoration => 16,
            ItemDetailType.MusicSheet => 8,
            ItemDetailType.Glider => 4,
            ItemDetailType.SlaveEquipment => 12,
            ItemDetailType.Location => 24,
            ItemDetailType.Opaque12 => 10,
            ItemDetailType.Opaque13 => 13,
            ItemDetailType.Opaque14 => 8,
            _ => 0
        };
    }

    public virtual void ReadDetails(PacketStream stream)
    {
        if (DetailType == ItemDetailType.Equipment)
        {
            Durability = stream.ReadByte();       // durability
            ChargeCount = stream.ReadInt16();     // chargeCount
            ChargeTime = stream.ReadDateTime();   // chargeTime
            TemperPhysical = stream.ReadUInt16(); // scaledA
            TemperMagical = stream.ReadUInt16();  // scaledB
            ChargeProcTime = stream.ReadDateTime();
            MappingFailBonus = stream.ReadByte();
            ElementLevel = stream.ReadByte();
            var gemValues = stream.ReadPiscW(18);
            GemIds = Array.ConvertAll(gemValues, value => checked((uint)value));
            return;
        }

        var detailLength = DetailPayloadLength(DetailType);
        if (detailLength > 0)
            Detail = stream.ReadBytes(detailLength);
    }

    public virtual void WriteDetails(PacketStream stream)
    {
        if (DetailType == ItemDetailType.Equipment)
        {
            stream.Write(Durability);     // durability
            stream.Write(ChargeCount);    // chargeCount
            stream.Write(ChargeTime);     // chargeTime
            stream.Write(TemperPhysical); // scaledA
            stream.Write(TemperMagical);  // scaledB
            stream.Write(ChargeProcTime);
            stream.Write(MappingFailBonus);
            stream.Write(ElementLevel);
            var gemValues = Array.ConvertAll(GemIds, value => (long)value);
            stream.WritePiscW(gemValues.Length, gemValues);
            return;
        }

        var detailLength = DetailPayloadLength(DetailType);
        if (detailLength <= 0)
            return;

        // Keep whatever came in for a variant we do not decode, rather than replacing it with
        // zeroes, so proxied and reloaded items go back out as they arrived.
        if ((Detail == null) || (Detail.Length != detailLength))
            Detail = new byte[detailLength];
        stream.Write(Detail);
    }

    public virtual bool HasFlag(ItemFlag flag)
    {
        return (ItemFlags & flag) == flag;
    }

    public virtual void SetFlag(ItemFlag flag)
    {
        ItemFlags |= flag;
    }

    public virtual void RemoveFlag(ItemFlag flag)
    {
        ItemFlags &= ~flag;
    }
}
