using System;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route;

public class Line : Patrol
{
    private float distance = 0f;
    private float MovingDistance = 0.27f;
    public Vector3 Position { get; set; }

    public override void Execute(Npc npc)
    {
        if (Position == default)
        {
            Stop(npc);
            return;
        }
        var move = false;
        var oldPosition = npc.Transform.Local.ClonePosition();
        var x = npc.Transform.Local.Position.X - Position.X;
        var y = npc.Transform.Local.Position.Y - Position.Y;
        var z = npc.Transform.Local.Position.Z - Position.Z;
        var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
        float tempMovingDistance;

        if (Math.Abs(x) > distance)
        {
            if (Math.Abs(MaxXYZ - Math.Abs(x)) > tolerance)
            {
                tempMovingDistance = Math.Abs(x) / (MaxXYZ / MovingDistance);
                tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
            }
            else
            {
                tempMovingDistance = MovingDistance;
            }

            if (x < 0)
            {
                npc.Transform.Local.Translate(tempMovingDistance, 0f, 0f);
            }
            else
            {
                npc.Transform.Local.Translate(-tempMovingDistance, 0f, 0f);
            }
            if (Math.Abs(x) < tempMovingDistance)
            {
                npc.Transform.Local.SetPosition(Position.X, npc.Transform.Local.Position.Y, npc.Transform.Local.Position.Z);
            }
            move = true;
        }
        if (Math.Abs(y) > distance)
        {
            if (Math.Abs(MaxXYZ - Math.Abs(y)) > tolerance)
            {
                tempMovingDistance = Math.Abs(y) / (MaxXYZ / MovingDistance);
                tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
            }
            else
            {
                tempMovingDistance = MovingDistance;
            }
            if (y < 0)
            {
                npc.Transform.Local.Translate(0f, tempMovingDistance, 0f);
            }
            else
            {
                npc.Transform.Local.Translate(0f, -tempMovingDistance, 0f);
            }
            if (Math.Abs(y) < tempMovingDistance)
            {
                npc.Transform.Local.SetPosition(npc.Transform.Local.Position.X, Position.Y, npc.Transform.Local.Position.Z);
            }
            move = true;
        }
        if (Math.Abs(z) > distance)
        {
            if (Math.Abs(MaxXYZ - Math.Abs(z)) > tolerance)
            {
                tempMovingDistance = Math.Abs(z) / (MaxXYZ / MovingDistance);
                tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
            }
            else
            {
                tempMovingDistance = MovingDistance;
            }
            if (z < 0)
            {
                npc.Transform.Local.Translate(0f, 0f, tempMovingDistance);
            }
            else
            {
                npc.Transform.Local.Translate(0f, 0f, -tempMovingDistance);
            }
            if (Math.Abs(z) < tempMovingDistance)
            {
                npc.Transform.Local.SetHeight(Position.Z);
            }
            move = true;
        }

        // 模拟unit
        // simulation unit
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        // 改变NPC坐标
        // Change the NPC coordinates
        moveType.X = npc.Transform.Local.Position.X;
        moveType.Y = npc.Transform.Local.Position.Y;
        if (npc.TemplateId == 13677 || npc.TemplateId == 13676) // swimming
        {
            moveType.Z = 98.5993f;
        }
        else if (npc.TemplateId == 13680) // shark
        {
            moveType.Z = 95.5993f;
        }
        else // other
        {
            moveType.Z = WorldManager.Instance.GetHeight(npc.Transform);
        }

        // looks in the direction of movement
        var angle = MathUtil.CalculateAngleFrom(npc.Transform.Local.Position.X, npc.Transform.Local.Position.Y, Position.X, Position.Y);
        var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
        moveType.RotationX = 0;
        moveType.RotationY = 0;
        moveType.RotationZ = rotZ;

        var displacement = npc.Transform.Local.Position - oldPosition;
        const float updateSeconds = 0.5f;
        moveType.VelX = move
            ? (short)Math.Clamp(displacement.X / updateSeconds / 60f * 32768f, short.MinValue, short.MaxValue)
            : (short)0;
        moveType.VelY = move
            ? (short)Math.Clamp(displacement.Y / updateSeconds / 60f * 32768f, short.MinValue, short.MaxValue)
            : (short)0;
        moveType.VelZ = move
            ? (short)Math.Clamp(displacement.Z / updateSeconds / 60f * 32768f, short.MinValue, short.MaxValue)
            : (short)0;
        moveType.ActorFlags = 0;
        moveType.Flags = 0;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = move ? (sbyte)127 : (sbyte)0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = 0;
        moveType.Alertness = 0;
        moveType.Time = unchecked((uint)Environment.TickCount64);

        if (move)
        {
            npc.CheckMovedPosition(oldPosition);
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            LoopDelay = 500;
            Repeat(npc);
        }
        else
        {
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            LoopAuto(npc);
        }
    }
}
