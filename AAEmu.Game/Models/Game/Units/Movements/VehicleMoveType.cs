using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements;

public class VehicleMoveType : MoveType
{
    /// <summary>The client clamps the wheel count to this before it reads the speeds.</summary>
    public const int MaxWheels = 18;

    public new short RotationX { get; set; }
    public new short RotationY { get; set; }
    public new short RotationZ { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }
    public float Steering { get; set; }

    /// <summary>
    /// One signed byte between steering and the wheel count. It was missing from both sides, so
    /// the wheel count was being read out of it.
    /// </summary>
    public sbyte Throttle { get; set; }

    public List<float> WheelAngVel { get; set; }

    public VehicleMoveType()
    {
        WheelAngVel = new List<float>();
    }

    public override void Read(PacketStream stream)
    {
        base.Read(stream);
        (X, Y, Z) = stream.ReadPosition();
        VelX = stream.ReadInt16();
        VelY = stream.ReadInt16();
        VelZ = stream.ReadInt16();
        RotationX = stream.ReadInt16();
        RotationY = stream.ReadInt16();
        RotationZ = stream.ReadInt16();

        AngVelX = stream.ReadSingle();
        AngVelY = stream.ReadSingle();
        AngVelZ = stream.ReadSingle();
        Steering = stream.ReadSingle();
        Throttle = stream.ReadSByte();

        // The count is what drives the loop, so a wrong reading here walks straight off the end
        // of the body. It used to be read out of the throttle byte, because that byte was not
        // being read at all.
        var wheelCount = stream.ReadByte();
        if (wheelCount > MaxWheels)
        {
            throw new InvalidOperationException(
                $"Vehicle movement claims {wheelCount} wheels, more than the {MaxWheels} the client reads");
        }

        for (var i = 0; i < wheelCount; i++)
        {
            WheelAngVel.Add(stream.ReadSingle());
        }
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

        stream.Write(AngVelX);
        stream.Write(AngVelY);
        stream.Write(AngVelZ);
        stream.Write(Steering);
        stream.Write(Throttle);

        // The client reads no more wheels than this and the count drives how much it reads, so a
        // longer list would leave it consuming whatever follows the body as wheel speeds.
        var wheels = Math.Min(WheelAngVel.Count, MaxWheels);
        stream.Write((byte)wheels);
        for (var i = 0; i < wheels; i++)
        {
            stream.Write(WheelAngVel[i]);
        }

        return stream;
    }
}
