using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCDoodadStreamPacket : StreamPacket
{
    private const int Protocol1810EntryLength = 32;

    private readonly int _id;
    private readonly int _next;
    private readonly Doodad[] _doodads;

    public TCDoodadStreamPacket(int id, int next, Doodad[] doodads) : base(TCOffsets.TCDoodadStreamPacket)
    {
        _id = id;
        _next = next;
        _doodads = doodads;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var bodyStart = stream.Count;
        stream.Write(_id);
        stream.Write(_next);
        stream.Write(_doodads.Length);

        foreach (var doodad in _doodads)
        {
            stream.WriteBc(doodad.ObjId);
            stream.Write(doodad.TemplateId);
            WriteProtocol1810Position(
                stream,
                doodad.Transform.World.Position.X,
                doodad.Transform.World.Position.Y,
                doodad.Transform.World.Position.Z);
            var (roll, pitch, yaw) = doodad.Transform.World.ToRollPitchYawShorts();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
            stream.Write(doodad.Scale);
            stream.Write(doodad.FuncGroupId);
        }

        var bodyLength = stream.Count - bodyStart;
        var expectedLength = 12 + _doodads.Length * Protocol1810EntryLength;
        if (bodyLength != expectedLength)
        {
            throw new InvalidOperationException(
                $"Invalid 10.8.1 doodad stream body length: actual={bodyLength}, expected={expectedLength}, count={_doodads.Length}");
        }

        Logger.Info(
            "TCDoodadStream 0x002: requestId={0}, next={1}, count={2}, bodyLen={3}",
            _id,
            _next,
            _doodads.Length,
            bodyLength);

        return stream;
    }

    private static void WriteProtocol1810Position(PacketStream stream, float x, float y, float z)
    {
        // Streamed doodads use 32-bit X/Y fixed point and a 24-bit Z value.
        stream.Write((int)(x * 512f));
        stream.Write((int)(y * 512f));

        var zRaw = (int)Math.Floor(((z + 100f) / 4196f * 4194304f) + 0.5f);
        zRaw = Math.Clamp(zRaw, 0, 0xFFFFFF);
        stream.Write(new[]
        {
            (byte)zRaw,
            (byte)(zRaw >> 8),
            (byte)(zRaw >> 16)
        });
    }
}
