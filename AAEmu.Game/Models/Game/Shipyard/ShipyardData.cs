using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Shipyard;

/// <summary>
/// A shipyard, as its state message carries it.
/// </summary>
/// <remarks>
/// The record was in the wrong order from end to end, and none of it had ever been read from this
/// client: it came from an emulator for an older version of the game and nobody checked it since.
/// Four of the numbers ride in one packed group behind a single header byte rather than lying flat;
/// the position follows that group instead of preceding it; and the owner is named twice, by
/// identity first and by name after, where only the name was going out.
///
/// Three fields carried the names <c>type</c>, <c>type2</c> and <c>type3</c> because whoever wrote
/// them did not know what they were. Two turn out to be places the client accepts and stores but
/// never consults; the third does not exist here at all. They are named for where they sit rather
/// than for what they do, which is at least honest.
///
/// The building stage is not part of this record - it follows it in the message that carries it.
/// </remarks>
public class ShipyardData : PacketMarshaler
{
    /// <summary>The shipyard's own lasting id - not the design, and not the item it was built from.</summary>
    public ulong Id { get; set; }

    public uint TemplateId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float zRot { get; set; }

    /// <summary>Kept by the client and read by nothing in it. Send zero.</summary>
    public int Pay { get; set; }

    /// <summary>How much building work has been done so far.</summary>
    public uint ActionsCompleted { get; set; }

    /// <summary>Kept by the client and read by nothing in it. Send zero.</summary>
    public uint Unknown34 { get; set; }

    /// <summary>
    /// Kept by the client and read by nothing in it. Send zero.
    /// </summary>
    /// <remarks>
    /// It was called health, and the design's own maximum multiplied by a hundred was going out in
    /// it, on the strength of the name alone. A shipyard's health travels the ordinary way every
    /// unit's does; this is not it.
    /// </remarks>
    public uint HpOrStatus { get; set; }

    /// <summary>The owner's identity. This is what ownership is decided by.</summary>
    public ulong OwnerId { get; set; }

    /// <summary>The owner's name, for showing. It decides nothing.</summary>
    public string OwnerName { get; set; }

    /// <summary>Accepted and kept by the client; nothing in it reads this back.</summary>
    public uint UnknownC4 { get; set; }

    public DateTime Spawned { get; set; }
    public uint ObjId { get; set; }

    /// <summary>
    /// The building stage: counted from zero while it is being built, and
    /// <see cref="FinishedStep"/> once it is done. Written after this record.
    /// </summary>
    public int Step { get; set; }

    /// <summary>
    /// The stage a finished shipyard reports.
    /// </summary>
    /// <remarks>
    /// Not one past the last stage, which is what was going out - a thousand, flat, whatever the
    /// design's stages number. There is no separate message for finishing: the last state carries
    /// the full count of work done and this.
    /// </remarks>
    public const int FinishedStep = 1000;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);                        // id : u64

        // Four numbers behind one header byte, two bits each, every one of them one to four bytes
        // wide according to what it holds.
        stream.WritePisc(TemplateId, ActionsCompleted, Unknown34, HpOrStatus);

        stream.Write(Helpers.ConvertLongX(X));   // x         : i64
        stream.Write(Helpers.ConvertLongY(Y));   // y         : i64
        stream.Write(Z);                         // z         : f32
        stream.Write(zRot);                      // zRot      : f32
        stream.Write(Pay);                       // pay       : i32
        stream.Write(OwnerId);                   // ownerId   : u64
        stream.Write(OwnerName ?? string.Empty); // ownerName : string, max 128
        stream.Write(UnknownC4);                 // u32
        stream.Write(Spawned);                   // spawned   : u64
        stream.WriteBc(ObjId);                   // objId     : 3 bytes

        return stream;
    }
}
