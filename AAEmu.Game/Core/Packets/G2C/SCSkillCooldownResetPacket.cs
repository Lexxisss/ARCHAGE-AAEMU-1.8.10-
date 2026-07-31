using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillCooldownResetPacket : GamePacket
{
    private Character _chr;
    private uint _skillId;
    private uint _tagId;
    private bool _gcd;

    public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {

    }

    public SCSkillCooldownResetPacket(Character chr, uint skillId, uint tagId, bool gcd) : base(SCOffsets.SCSkillCooldownResetPacket, 5)
    {
        _skillId = skillId;
        _tagId = tagId;
        _gcd = gcd;
        _chr = chr;
        _gcd = gcd;
    }

    /// <summary>
    /// Verified against the target x2game.dll (serializer 0x399D4050, vtable 0x39D58CB0):
    /// the body is 15 bytes - bc3 id, two u32, then four separate bytes. Only the first of
    /// those four is the GCD flag; the other three were missing entirely, which left every
    /// reset packet three bytes short.
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_chr.ObjId); // entity id, bc3
        stream.Write(_skillId);     // type A
        stream.Write(_tagId);       // type B
        stream.Write(_gcd);         // gc - trigger GCD

        // rstc / rtsc / rtstc. The serializer exposes no semantics beyond the labels, so
        // they stay zero until a capture shows otherwise.
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)0);

        return stream;
    }
}
