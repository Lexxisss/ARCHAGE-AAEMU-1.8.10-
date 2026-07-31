using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The optional f/c/e/p/d block carried by SCSkillStarted and SCSkillFired, verified against
/// the target x2game.dll helper at 0x399CC730.
///
/// It is a sparse structure, not a single boolean: a presence mask byte followed only by the
/// fields the mask selects, so it ranges from 1 to 9 bytes. An empty block is the mask alone,
/// which is why writing a plain zero byte happened to be correct while nothing was set.
/// </summary>
/// <remarks>
/// The <c>d</c> field defaults to 1 in the client, so its presence bit is set when the value
/// is 0 - the mask advertises a deviation from the default rather than the presence of data.
/// </remarks>
public struct SkillExtraData
{
    private const byte HasC = 0x01;
    private const byte HasE = 0x02;
    private const byte HasP = 0x04;
    private const byte HasD = 0x08;

    public byte C { get; set; }
    public ushort E { get; set; }
    public uint P { get; set; }

    /// <summary>Client-side default is 1; only a zero is transmitted.</summary>
    public bool D { get; set; }

    public static SkillExtraData Default => new() { D = true };

    public readonly void Write(PacketStream stream)
    {
        byte mask = 0;
        if (C != 0)
            mask |= HasC;
        if (E != 0)
            mask |= HasE;
        if (P != 0)
            mask |= HasP;
        if (!D)
            mask |= HasD;

        stream.Write(mask);

        if ((mask & HasC) != 0)
            stream.Write(C);
        if ((mask & HasE) != 0)
            stream.Write(E);
        if ((mask & HasP) != 0)
            stream.Write(P);
        if ((mask & HasD) != 0)
            stream.Write((byte)0);
    }
}
