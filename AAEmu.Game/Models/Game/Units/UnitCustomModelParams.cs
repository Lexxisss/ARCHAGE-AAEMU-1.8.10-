using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units;

public enum UnitCustomModelType
{
    None = 0,
    Hair = 1,
    Skin = 2,
    Face = 3
}

public class FixedDecalAsset : PacketMarshaler
{
    public uint AssetId { get; set; }
    public float AssetWeight { get; set; }

    public FixedDecalAsset(uint assetId = 0, float assetWeight = 0)
    {
        AssetId = assetId;
        AssetWeight = assetWeight;
    }

    public override void Read(PacketStream stream)
    {
        AssetId = stream.ReadUInt32();
        AssetWeight = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(AssetId);
        stream.Write(AssetWeight);
        return stream;
    }
}

public class FaceModel : PacketMarshaler
{
    public uint MovableDecalAssetId { get; set; }
    public float MovableDecalWeight { get; set; }
    public float MovableDecalScale { get; set; }
    public float MovableDecalRotate { get; set; }
    public short MovableDecalMoveX { get; set; }
    public short MovableDecalMoveY { get; set; }

    public FixedDecalAsset[] FixedDecalAsset { get; }

    public uint DiffuseMapId { get; set; }
    public uint NormalMapId { get; set; }
    public uint EyelashMapId { get; set; }
    public float NormalMapWeight { get; set; }
    public uint LipColor { get; set; }
    public uint LeftPupilColor { get; set; }
    public uint RightPupilColor { get; set; }
    public uint EyebrowColor { get; set; }
    public uint DecoColor { get; set; }

    public byte[] Modifier { get; set; }
    public byte VisualRace { get; set; }
    public byte VisualGender { get; set; }
    public System.DateTime VisualRaceExpiredTime { get; set; }
    public byte BaseRace { get; set; }
    public byte BaseGender { get; set; }
    public uint WingColor { get; set; }
    public byte WingScale { get; set; } = 100;
    public sbyte WingOffsetX { get; set; }
    public sbyte WingOffsetY { get; set; }
    public sbyte WingOffsetZ { get; set; }

    public FaceModel()
    {
        FixedDecalAsset = new FixedDecalAsset[6];   // 6 - 3.0.3.0, 4 - 1.2
        for (var i = 0; i < FixedDecalAsset.Length; i++)
            FixedDecalAsset[i] = new FixedDecalAsset();

        Modifier = new byte[128];
    }

    public bool SetFixedDecalAsset(byte index, uint id, float weight)
    {
        if (FixedDecalAsset.Length <= index)
            return false;

        FixedDecalAsset[index].AssetId = id;
        FixedDecalAsset[index].AssetWeight = weight;

        return true;
    }

    /// <summary>
    /// Creates the complete Face payload used by an NPC SCUnitState while keeping
    /// the unit on its ordinary race/equipment model path. In target x2game.dll
    /// 0x39580C60, VisualRace != 0 becomes the visual-race-active flag passed to
    /// equipment construction at 0x39578D30. BaseRace/BaseGender describe the
    /// ordinary model; VisualRace/VisualGender are only for a temporary visual-race
    /// transformation and therefore must remain zero for a normal NPC.
    /// </summary>
    public FaceModel CloneForNpcWire(byte baseRace, byte baseGender)
    {
        var modifierCopy = (byte[])NormalizeModifier(Modifier).Clone();
        var clone = new FaceModel
        {
            MovableDecalAssetId = MovableDecalAssetId,
            MovableDecalWeight = MovableDecalWeight,
            MovableDecalScale = MovableDecalScale,
            MovableDecalRotate = MovableDecalRotate,
            MovableDecalMoveX = MovableDecalMoveX,
            MovableDecalMoveY = MovableDecalMoveY,
            DiffuseMapId = DiffuseMapId,
            NormalMapId = NormalMapId,
            EyelashMapId = EyelashMapId,
            NormalMapWeight = NormalMapWeight,
            LipColor = LipColor,
            LeftPupilColor = LeftPupilColor,
            RightPupilColor = RightPupilColor,
            EyebrowColor = EyebrowColor,
            DecoColor = DecoColor,
            Modifier = modifierCopy,
            BaseRace = baseRace,
            BaseGender = baseGender,
            VisualRaceExpiredTime = default,
            VisualRace = 0,
            VisualGender = 0,
            WingColor = WingColor,
            WingScale = WingScale,
            WingOffsetX = WingOffsetX,
            WingOffsetY = WingOffsetY,
            WingOffsetZ = WingOffsetZ
        };

        for (var index = 0; index < FixedDecalAsset.Length; index++)
            clone.SetFixedDecalAsset((byte)index, FixedDecalAsset[index].AssetId, FixedDecalAsset[index].AssetWeight);

        return clone;
    }

    public override void Read(PacketStream stream)
    {
        MovableDecalAssetId = stream.ReadUInt32(); // type
        MovableDecalWeight = stream.ReadSingle();  // weight
        MovableDecalScale = stream.ReadSingle();   // scale
        MovableDecalRotate = stream.ReadSingle();  // rotate
        MovableDecalMoveX = stream.ReadInt16();    // moveX
        MovableDecalMoveY = stream.ReadInt16();    // moveY

        // --- begin pish
        var mAssets = stream.ReadPisc(4);
        // --- end pish
        FixedDecalAsset[0].AssetId = (uint)mAssets[0];
        FixedDecalAsset[1].AssetId = (uint)mAssets[1];
        FixedDecalAsset[2].AssetId = (uint)mAssets[2];
        FixedDecalAsset[3].AssetId = (uint)mAssets[3];

        // --- begin pish
        mAssets = stream.ReadPisc(2);
        // --- end pish
        FixedDecalAsset[4].AssetId = (uint)mAssets[0];
        FixedDecalAsset[5].AssetId = (uint)mAssets[1];

        // --- begin pish
        var mMap = stream.ReadPisc(3);
        // --- end pish
        DiffuseMapId = (uint)mMap[0];
        NormalMapId = (uint)mMap[1];
        EyelashMapId = (uint)mMap[2];

        for (var i = 0; i < 6; i++)
        {
            FixedDecalAsset[i].AssetWeight = stream.ReadSingle(); // weight
        }

        NormalMapWeight = stream.ReadSingle();    // weight
        LipColor = stream.ReadUInt32();           // lip
        LeftPupilColor = stream.ReadUInt32();     // leftPupil
        RightPupilColor = stream.ReadUInt32();    // rightPupil
        EyebrowColor = stream.ReadUInt32();       // eyebrow
        DecoColor = stream.ReadUInt32();          // deco

        // Target client prefixes modifier with its ushort byte length (normally 128).
        // Read the prefix so BaseRace and every following appearance field remain aligned.
        Modifier = stream.ReadBytes();
        BaseRace = stream.ReadByte();
        BaseGender = stream.ReadByte();
        VisualRaceExpiredTime = stream.ReadDateTime();
        VisualRace = stream.ReadByte();
        VisualGender = stream.ReadByte();
        WingColor = stream.ReadUInt32();
        WingScale = stream.ReadByte();
        WingOffsetX = stream.ReadSByte();
        WingOffsetY = stream.ReadSByte();
        WingOffsetZ = stream.ReadSByte();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(MovableDecalAssetId);
        stream.Write(MovableDecalWeight);
        stream.Write(MovableDecalScale);
        stream.Write(MovableDecalRotate);
        stream.Write(MovableDecalMoveX);
        stream.Write(MovableDecalMoveY);
        stream.WritePisc(FixedDecalAsset[0].AssetId, FixedDecalAsset[1].AssetId, FixedDecalAsset[2].AssetId, FixedDecalAsset[3].AssetId);
        stream.WritePisc(FixedDecalAsset[4].AssetId, FixedDecalAsset[5].AssetId);
        stream.WritePisc(DiffuseMapId, NormalMapId, EyelashMapId);
        for (var i = 0; i < 6; i++)
        {
            stream.Write(FixedDecalAsset[i].AssetWeight); // weight
        }
        stream.Write(NormalMapWeight);
        stream.Write(LipColor);
        stream.Write(LeftPupilColor);
        stream.Write(RightPupilColor);
        stream.Write(EyebrowColor);
        stream.Write(DecoColor);

        // Target client expects a ushort size before the modifier bytes.
        // Keep the payload normalized to 128 bytes and restore the two-byte prefix.
        stream.Write(NormalizeModifier(Modifier), true);
        // Target UnitCustomModel serializer 0x39969450 includes this
        // visual-race/wing tail in SCUnitState immediately after Modifier.
        stream.Write(BaseRace);
        stream.Write(BaseGender);
        stream.Write(VisualRaceExpiredTime);
        stream.Write(VisualRace);
        stream.Write(VisualGender);
        stream.Write(WingColor);
        // Captured target characters use 100 as the neutral/default scale.
        // Older database blobs can contain zero because this tail was not
        // previously persisted by all serializers.
        stream.Write(WingScale == 0 ? (byte)100 : WingScale);
        stream.Write(WingOffsetX);
        stream.Write(WingOffsetY);
        stream.Write(WingOffsetZ);
        return stream;
    }

    /// <summary>
    /// Compatibility alias. SCUnitState 0x0133 uses the same complete target
    /// FaceModel form as Write(), including the visual-race/wing tail.
    /// </summary>
    public PacketStream Write1810(PacketStream stream) => Write(stream);

    private static byte[] NormalizeModifier(byte[] modifier)
    {
        if (modifier is { Length: 128 })
            return modifier;

        var normalized = new byte[128];
        if (modifier != null)
            System.Array.Copy(modifier, normalized, System.Math.Min(modifier.Length, normalized.Length));
        return normalized;
    }

}

public class UnitCustomModelParams : PacketMarshaler
{
    private UnitCustomModelType _type;
    public UnitCustomModelType Type => _type;
    public uint Id { get; private set; }
    /// <summary>
    /// Target appearance field +0x0C. This is not the NPC model_id.
    /// The target total-custom copier leaves it untouched and the client-created
    /// control packet uses zero.
    /// </summary>
    public uint BodyDiffuseOrModelDefaultId { get; private set; }

    // Compatibility alias for older code. Do not feed TotalCharacterCustom.ModelId here.
    public uint ModelId => BodyDiffuseOrModelDefaultId;
    public uint BodyNormalMapId { get; private set; }
    public uint HairColorId { get; private set; }
    public uint HornColorId { get; private set; }
    public uint SkinColorId { get; private set; }
    public float BodyNormalMapWeight { get; private set; }
    public FaceModel Face { get; private set; }
    public uint DefaultHairColor { get; private set; }
    public uint TwoToneHair { get; private set; }
    public float TwoToneFirstWidth { get; private set; }
    public float TwoToneSecondWidth { get; private set; }

    public UnitCustomModelParams(UnitCustomModelType type = UnitCustomModelType.None)
    {
        SetType(type);
    }

    public UnitCustomModelParams SetId(uint id)
    {
        Id = id;
        return this;
    }

    public UnitCustomModelParams SetType(UnitCustomModelType type)
    {
        _type = type;
        if (_type == UnitCustomModelType.Face)
            Face = new FaceModel();
        return this;
    }

    public UnitCustomModelParams SetBodyDiffuseOrModelDefaultId(uint value)
    {
        BodyDiffuseOrModelDefaultId = value;
        return this;
    }

    /// <summary>
    /// Compatibility alias. The value is the target +0x0C appearance slot,
    /// not the actor/NPC model id.
    /// </summary>
    public UnitCustomModelParams SetModelId(uint value)
    {
        return SetBodyDiffuseOrModelDefaultId(value);
    }

    public UnitCustomModelParams SetBodyNormalMapId(uint bodyNormalMapId)
    {
        BodyNormalMapId = bodyNormalMapId;
        return this;
    }

    public UnitCustomModelParams SetBodyNormalMapWeight(float weight)
    {
        BodyNormalMapWeight = weight;
        return this;
    }

    public UnitCustomModelParams SetDefaultHairColor(uint defaultHairColor)
    {
        DefaultHairColor = defaultHairColor;
        return this;
    }

    public UnitCustomModelParams SetHairColorId(uint hairColorId)
    {
        HairColorId = hairColorId;
        return this;
    }

    public UnitCustomModelParams SetHornColorId(uint hornColorId)
    {
        HornColorId = hornColorId;
        return this;
    }

    public UnitCustomModelParams SetSkinColorId(uint skinColorId)
    {
        SkinColorId = skinColorId;
        return this;
    }

    public UnitCustomModelParams SetTwoToneFirstWidth(float twoToneFirstWidth)
    {
        TwoToneFirstWidth = twoToneFirstWidth;
        return this;
    }

    public UnitCustomModelParams SetTwoToneHair(uint twoToneHair)
    {
        TwoToneHair = twoToneHair;
        return this;
    }

    public UnitCustomModelParams SetTwoToneSecondWidth(float twoToneSecondWidth)
    {
        TwoToneSecondWidth = twoToneSecondWidth;
        return this;
    }

    public UnitCustomModelParams SetFace(FaceModel face)
    {
        Face = face;
        return this;
    }

    /// <summary>
    /// Deep packet-local copy for a normal humanoid NPC. All authored appearance
    /// values are preserved, including skin tone, face maps, pupils, decals and the
    /// 128-byte modifier. Only the visual-race transformation tail is neutralized
    /// by FaceModel.CloneForNpcWire.
    /// </summary>
    public UnitCustomModelParams CloneForNpcWire(byte baseRace, byte baseGender)
    {
        if (_type != UnitCustomModelType.Face || Face == null)
            return new UnitCustomModelParams(UnitCustomModelType.None);

        return new UnitCustomModelParams(UnitCustomModelType.Face)
            .SetId(Id)
            .SetHairColorId(HairColorId)
            .SetSkinColorId(SkinColorId)
            .SetBodyDiffuseOrModelDefaultId(BodyDiffuseOrModelDefaultId)
            .SetHornColorId(HornColorId)
            .SetDefaultHairColor(DefaultHairColor)
            .SetTwoToneHair(TwoToneHair)
            .SetTwoToneFirstWidth(TwoToneFirstWidth)
            .SetTwoToneSecondWidth(TwoToneSecondWidth)
            .SetBodyNormalMapId(BodyNormalMapId)
            .SetBodyNormalMapWeight(BodyNormalMapWeight)
            .SetFace(Face.CloneForNpcWire(baseRace, baseGender));
    }

    public override void Read(PacketStream stream)
    {
        SetType((UnitCustomModelType)stream.ReadByte());
        if (_type == UnitCustomModelType.None)
            return;

        // Exact 10.8.1.0 conditional order confirmed in compiled x2game.dll
        // serializer 0x39969450.
        HairColorId = stream.ReadUInt32();
        if (_type == UnitCustomModelType.Hair)
            return;

        // Mirrors Write. Both sides of the wire use one function, so what a creation request
        // carries and what a unit state carries are the same shape - which makes the client's own
        // packets a control sample for both.
        SkinColorId = stream.ReadUInt32();
        BodyDiffuseOrModelDefaultId = stream.ReadUInt32(); // +0x0C, not NPC model_id
        if (_type == UnitCustomModelType.Skin)
            return;

        HornColorId = stream.ReadUInt32();
        DefaultHairColor = stream.ReadUInt32();
        TwoToneHair = stream.ReadUInt32();
        TwoToneFirstWidth = stream.ReadSingle();
        TwoToneSecondWidth = stream.ReadSingle();
        BodyNormalMapId = stream.ReadUInt32();
        BodyNormalMapWeight = stream.ReadSingle();
        Face.Read(stream);
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);
        if (_type == UnitCustomModelType.None)
            return stream;

        stream.Write(HairColorId);
        if (_type == UnitCustomModelType.Hair)
            return stream;

        // Skin, then a slot the client fills from its own defaults, then horns. The horn colour was
        // going out where the skin is read and the skin one place further on, so every person was
        // described with a skin colour of nothing - and there is no colour numbered nothing; the
        // table starts at one. A colour that cannot be looked up leaves the client holding a
        // stand-in, which is what black hands are.
        stream.Write(SkinColorId);
        stream.Write(BodyDiffuseOrModelDefaultId); // +0x0C; zero in target client-created packets
        if (_type == UnitCustomModelType.Skin)
            return stream;

        stream.Write(HornColorId);
        stream.Write(DefaultHairColor);
        stream.Write(TwoToneHair);
        stream.Write(TwoToneFirstWidth);
        stream.Write(TwoToneSecondWidth);
        stream.Write(BodyNormalMapId);
        stream.Write(BodyNormalMapWeight);
        stream.Write(Face);
        return stream;
    }

    /// <summary>
    /// Compatibility alias. Target SCUnitState uses the complete serializer,
    /// including the 20-byte FaceModel visual-race/wing tail.
    /// </summary>
    public PacketStream Write1810(PacketStream stream) => Write(stream);

    private void ReadLegacy3030(PacketStream stream)
    {
        SetType((UnitCustomModelType)stream.ReadByte()); // ext

        if (_type == UnitCustomModelType.None) { return; }

        // Hair
        HairColorId = stream.ReadUInt32();        // HairColorId type
        HornColorId = stream.ReadUInt32();        // HornColorId type for 3.0.3.0
        DefaultHairColor = stream.ReadUInt32();   // defaultHairColor for 3.0.3.0
        TwoToneHair = stream.ReadUInt32();        // twoToneHair for 3.0.3.0
        TwoToneFirstWidth = stream.ReadSingle();  // twoToneFirstWidth for 3.0.3.0
        TwoToneSecondWidth = stream.ReadSingle(); // twoToneSecondWidth for 3.0.3.0

        if (_type == UnitCustomModelType.Hair) { return; }

        SkinColorId = stream.ReadUInt32();          // type
        BodyDiffuseOrModelDefaultId = stream.ReadUInt32(); // legacy +0x0C slot
        BodyNormalMapId = stream.ReadUInt32();      // type for 3.0.3.0
        BodyNormalMapWeight = stream.ReadSingle();  // weight

        if (_type == UnitCustomModelType.Skin) { return; }

        // Face
        Face.Read(stream);
    }

    private PacketStream WriteLegacy3030(PacketStream stream)
    {
        stream.Write((byte)_type); // ext

        if (_type == UnitCustomModelType.None) { return stream; }

        stream.Write(HairColorId);        // type
        stream.Write(HornColorId);        // type for 3.0.3.0
        stream.Write(DefaultHairColor);   // defaultHairColor for 3.0.3.0
        stream.Write(TwoToneHair);        // twoToneHair for 3.0.3.0
        stream.Write(TwoToneFirstWidth);  // twoToneFirstWidth for 3.0.3.0
        stream.Write(TwoToneSecondWidth); // twoToneSecondWidth for 3.0.3.0

        if (_type == UnitCustomModelType.Hair) { return stream; }

        stream.Write(SkinColorId);          // type
        stream.Write(BodyDiffuseOrModelDefaultId); // legacy +0x0C slot
        stream.Write(BodyNormalMapId);      // type for 3.0.3.0
        stream.Write(BodyNormalMapWeight);  // weight

        if (_type == UnitCustomModelType.Skin) { return stream; }

        stream.Write(Face);

        return stream;
    }
}
