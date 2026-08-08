using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public enum PlotObjectType : byte
{
    UNIT = 0x1,
    POSITION = 0x2
}

public class PlotObject : PacketMarshaler
{
    public PlotObjectType Type { get; set; }
    public uint UnitId { get; set; }
    public Transform Position { get; set; }
    public Transform LinePosition { get; set; }
    public uint PositionId0 { get; set; }
    public uint PositionId1 { get; set; }
    public uint PositionId2 { get; set; }

    public PlotObject(BaseUnit unit)
    {
        Type = PlotObjectType.UNIT;
        UnitId = unit.ObjId;
    }

    public PlotObject(uint unitId)
    {
        Type = PlotObjectType.UNIT;
        UnitId = unitId;
    }

    /// <summary>
    /// Creates the target 1.8.1.0 POSITION plot object.
    /// </summary>
    /// <remarks>
    /// The wire object contains two complete compressed transforms: the endpoint (<c>pos</c>)
    /// and the line/source transform (<c>linePos</c>), followed by three BC identifiers. Older
    /// code stopped after the first transform, shifting every field that followed in
    /// SCPlotEventPacket.
    /// </remarks>
    public PlotObject(Transform position, Transform linePosition = null,
        uint positionId0 = 0, uint positionId1 = 0, uint positionId2 = 0)
    {
        Type = PlotObjectType.POSITION;
        Position = position.CloneDetached();
        LinePosition = (linePosition ?? position).CloneDetached();
        PositionId0 = positionId0;
        PositionId1 = positionId1;
        PositionId2 = positionId2;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)Type);

        switch (Type)
        {
            case PlotObjectType.UNIT:
                stream.WriteBc(UnitId);
                break;
            case PlotObjectType.POSITION:
                WritePositionAndRotation(stream, Position);     // pos + rot
                WritePositionAndRotation(stream, LinePosition); // linePos + lineRot
                stream.WriteBc(PositionId0);
                stream.WriteBc(PositionId1);
                stream.WriteBc(PositionId2);
                break;
        }

        return stream;
    }

    private static void WritePositionAndRotation(PacketStream stream, Transform transform)
    {
        // Eleven bytes, not nine: the client reads each coordinate as four bytes and the last as
        // three - target x2game.dll 0x399653A0, dev 0x39B47020, both eleven byte reads. The nine
        // byte form left this object four bytes short, and with two transforms in it that is what
        // every field after them was out by.
        stream.WriteWorldPosition(
            transform.Local.Position.X,
            transform.Local.Position.Y,
            transform.Local.Position.Z);
        // TARGET/DEV decode these three bytes as one axis-angle rotation vector and rebuild a
        // quaternion from it. They are not three independently quantized Euler angles. Use the
        // same codec as Unit movement so combined roll/pitch/yaw (glider Leap in particular)
        // preserves the intended heading. TARGET decode: 0x39635870; DEV: 0x39390CF0.
        var rotation = transform.Local.ToRollPitchYawSBytesMovement();
        stream.Write(rotation.Item1); // rot.x : i8 rotation-vector component
        stream.Write(rotation.Item2); // rot.y : i8 rotation-vector component
        stream.Write(rotation.Item3); // rot.z : i8 rotation-vector component
    }
}
