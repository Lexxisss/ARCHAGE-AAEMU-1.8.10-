using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 NPC auxiliary state sent between SCUnitState (0x0133) and
/// SCOneUnitMovement (0x01E4). Opcode 0x0321 is the target
/// SCBuffCreated packet, but the initial NPC visibility form is always a
/// fixed 39-byte record and may carry buffId=0 when no active template buff
/// exists.
/// </summary>
public class SCNpcInitialBuffPacket : GamePacket
{
    private readonly Npc _npc;

    public SCNpcInitialBuffPacket(Npc npc)
        : base(SCOffsets.SCBuffCreatedPacket, 5)
    {
        _npc = npc;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var buff = ResolveInitialBuff(_npc);
        var buffId = buff?.Template?.BuffId ?? 0u;

        // Exact source-generated target layout. The caster type is zero, but
        // its three-byte object id is the NPC itself, not zero.
        stream.Write((byte)0);
        stream.WriteBc(_npc.ObjId);

        stream.Write(0u);           // caster persistent characterId
        stream.Write(0u);           // target 10.x reserved
        stream.WriteBc(_npc.ObjId); // targetId
        stream.Write(3u);           // initial visibility buff index
        stream.Write(buffId);       // active non-passive template buff, or zero
        stream.Write((byte)0);      // source level in donor-generated initial form
        stream.Write((short)1);     // source ability level
        stream.Write(0u);           // source skillId
        stream.Write((byte)1);      // stack
        stream.Write(0L);           // initial buff runtime data

        return stream;
    }

    private static Buff ResolveInitialBuff(Npc npc)
    {
        if (npc?.Template?.Buffs == null || npc.Buffs == null)
            return null;

        foreach (var buffId in npc.Template.Buffs)
        {
            if (buffId == 0)
                continue;

            var buff = npc.Buffs.GetEffectFromBuffId(buffId);
            if (buff != null && !buff.Passive)
                return buff;
        }

        return null;
    }
}
