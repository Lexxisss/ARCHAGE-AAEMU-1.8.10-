using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
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

        // This channel is meant for the world's own furniture, which the client already has in
        // its map data. Anything a player put there is ours alone, and the state we name for it
        // changes as it grows - which is the one thing that differs between a plant the client
        // reads happily and one it hangs on. Name them, so the next hang has a suspect.
        foreach (var doodad in _doodads)
        {
            if (doodad.OwnerId == 0 && doodad.PlantTime == DateTime.MinValue)
                continue;

            Logger.Warn(
                "TCDoodadStream carries a player-placed object: objId={0}, template={1}, funcGroup={2}, " +
                "owner={3}, item={4}, planted={5:u}, scale={6}",
                doodad.ObjId,
                doodad.TemplateId,
                doodad.FuncGroupId,
                doodad.OwnerId,
                doodad.ItemTemplateId,
                doodad.PlantTime,
                doodad.Scale);
        }

        return stream;
    }

    /// <summary>
    /// The eleven-byte packed position, the same one the rest of this family uses.
    /// </summary>
    /// <remarks>
    /// Written as two plain signed integers here for a while, which is a different encoding
    /// entirely: the real one carries magnitudes and keeps each sign as a bit in the last byte,
    /// alongside the top of the height. A negative coordinate came out as an enormous positive
    /// one, and the height shared its byte with nothing.
    /// </remarks>
    private static void WriteProtocol1810Position(PacketStream stream, float x, float y, float z)
    {
        var xFixed = (long)(x * 512f);
        var yFixed = (long)(y * 512f);

        stream.Write((uint)Math.Min(uint.MaxValue, Math.Abs(xFixed)));
        stream.Write((uint)Math.Min(uint.MaxValue, Math.Abs(yFixed)));

        var clampedZ = Math.Clamp(z, Helpers.MinPackedHeight, Helpers.MaxPackedHeight);
        var zCode = (uint)Math.Clamp((long)MathF.Floor(((clampedZ + 100f) / 4196f * 4194304f) + 0.5f), 0L, 0x3F_FFFFL);

        stream.Write((ushort)(zCode & 0xFFFF));

        var high = (byte)((zCode >> 16) & 0x3F);
        if (yFixed < 0)
            high |= 0x40;
        if (xFixed < 0)
            high |= 0x80;
        stream.Write(high);
    }
}
