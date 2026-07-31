using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements;

public class UnitMoveType : MoveType
{
    public sbyte[] DeltaMovement { get; set; }

    /// <summary>
    /// Posture, not combat state. The client's enum is
    /// <c>0 Stand, 1 Crouch, 2 Prone, 3 Relaxed, 4 Stealth, 5 Swim, 6 ZeroG</c>; no other
    /// value is known to be accepted. Comments throughout this branch used to read
    /// "COMBAT = 0, IDLE = 1", so walking NPCs were announced as crouching and the client
    /// posed them accordingly instead of playing a walk cycle.
    /// </summary>
    public sbyte Stance { get; set; }
    public sbyte Alertness { get; set; }
    public byte GcFlags { get; set; }
    public ushort GcPart { get; set; }
    public ushort GcPartId { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float Z2 { get; set; }
    public sbyte RotationX2 { get; set; }
    public sbyte RotationY2 { get; set; }
    public sbyte RotationZ2 { get; set; }
    public uint ClimbData { get; set; }
    public uint GcId { get; set; }
    public ushort FallVel { get; set; }
    /// <summary>
    /// Gate word for the optional blocks below, and nothing else. It carries no walk/run or
    /// stand-still meaning, despite what the call sites used to assume.
    /// </summary>
    public ushort ActorFlags { get; set; }

    public uint MaxPushedUnitId { get; set; }

    /// <summary>Present only under <c>ActorFlags &amp; 0x8000</c>.</summary>
    public byte SubType { get; set; }

    public short SubPosX { get; set; }
    public short SubPosY { get; set; }
    public short SubPosZ { get; set; }

    /// <summary>Written only for <see cref="SubType"/> 1, 2 or 3.</summary>
    public uint SubTypeId { get; set; }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        (X, Y, Z) = stream.ReadPosition();
        VelX = stream.ReadInt16();
        VelY = stream.ReadInt16();
        VelZ = stream.ReadInt16();
        RotationX = stream.ReadSByte();
        RotationY = stream.ReadSByte();
        RotationZ = stream.ReadSByte();
        DeltaMovement = new sbyte[3];
        DeltaMovement[0] = stream.ReadSByte();
        DeltaMovement[1] = stream.ReadSByte();
        DeltaMovement[2] = stream.ReadSByte();
        Stance = stream.ReadSByte();
        Alertness = stream.ReadSByte();
        ActorFlags = stream.ReadUInt16(); // ushort in 3.0.3.0, sbyte in 1.2
        if ((ActorFlags & 0x80) == 0x80)
            FallVel = stream.ReadUInt16(); // actor.fallVel
        if ((ActorFlags & 0x20) == 0x20) // TODO если находится на движущейся повозке/лифте/корабле, то здесь координаты персонажа
        {
            GcFlags = stream.ReadByte();    // actor.gcFlags
            GcPart = stream.ReadUInt16();   // actor.gcPart
            GcPartId = stream.ReadUInt16(); // actor.gcPartId
            (X2, Y2, Z2) = stream.ReadPosition(); // ix, iy, iz
            RotationX2 = stream.ReadSByte();
            RotationY2 = stream.ReadSByte();
            RotationZ2 = stream.ReadSByte();
        }
        if ((ActorFlags & 0x60) != 0)
            GcId = stream.ReadUInt32();            // actor.gcId
        if ((ActorFlags & 0x40) == 0x40 || (ActorFlags & 0x8000) == 0x8000)
            ClimbData = stream.ReadUInt32();       // actor.climbData
        if ((ActorFlags & 0x8000) == 0x8000)
        {
            SubType = stream.ReadByte();           // actor.subType
            SubPosX = stream.ReadInt16();          // scaled by 0.01 once decoded
            SubPosY = stream.ReadInt16();
            SubPosZ = stream.ReadInt16();
            if (SubType is 1 or 2 or 3)
                SubTypeId = stream.ReadUInt32();
        }
        if ((ActorFlags & 0x100) == 0x100)
            MaxPushedUnitId = stream.ReadUInt32(); // actor.maxPushedUnitId
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.WriteWorldPosition(X, Y, Z);
        stream.Write(VelX);
        stream.Write(VelY);
        stream.Write(VelZ);
        stream.Write(RotationX);
        stream.Write(RotationY);
        stream.Write(RotationZ);
        stream.Write(DeltaMovement[0]);
        stream.Write(DeltaMovement[1]);
        stream.Write(DeltaMovement[2]);
        stream.Write(Stance);
        stream.Write(Alertness);
        stream.Write(ActorFlags);
        if ((ActorFlags & 0x80) == 0x80)
            stream.Write(FallVel);
        if ((ActorFlags & 0x20) == 0x20)
        {
            stream.Write(GcFlags);
            stream.Write(GcPart);
            stream.Write(GcPartId);
            stream.WriteWorldPosition(X2, Y2, Z2);
            stream.Write(RotationX2);
            stream.Write(RotationY2);
            stream.Write(RotationZ2);
        }
        if ((ActorFlags & 0x60) != 0)
            stream.Write(GcId);
        if ((ActorFlags & 0x40) == 0x40 || (ActorFlags & 0x8000) == 0x8000)
            stream.Write(ClimbData);
        if ((ActorFlags & 0x8000) == 0x8000)
        {
            stream.Write(SubType);
            stream.Write(SubPosX);
            stream.Write(SubPosY);
            stream.Write(SubPosZ);
            if (SubType is 1 or 2 or 3)
                stream.Write(SubTypeId);
        }
        if ((ActorFlags & 0x100) == 0x100)
            stream.Write(MaxPushedUnitId);
        return stream;
    }
}
