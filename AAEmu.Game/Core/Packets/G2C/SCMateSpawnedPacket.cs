using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces a summoned pet or mount to its owner.
/// </summary>
/// <remarks>
/// Payload is exactly 64 bytes:
///
///     tl:u16, mateCategory:u8, templateType:u32, itemId:u64, userState:u8,
///     exp:i32, spawnDelayTime:u32, skills[10]:u32
///
/// A mileage field used to be written between the experience and the spawn delay. It does not
/// exist here, and the four extra bytes pushed the delay and all ten skill slots out of place -
/// which is why a summoned mount arrived with nothing usable on it.
///
/// The skill list is a fixed ten entries, padded with zeroes rather than length-prefixed.
///
/// This message does not create the animal - it creates the client's record of it, filed under
/// the handle. Nothing in the world-state message carries that handle, so the only thing tying
/// the two together is the template: the client keeps a template-to-handle index and looks the
/// record up through it. Sending the player's own row id here instead of the template left the
/// index pointing at nothing, so the animal stood in the world with no record behind it - no
/// skill bar, and nothing to ride.
/// </remarks>
public class SCMateSpawnedPacket : GamePacket
{
    private const int SkillSlots = 10;

    private readonly Mate _mate;

    public SCMateSpawnedPacket(Mate mate) : base(SCOffsets.SCMateSpawnedPacket, 5)
    {
        _mate = mate;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_mate.TlId);              // tl             : u16
        stream.Write((byte)_mate.MateType);    // mateCategory   : u8
        stream.Write(_mate.TemplateId);        // templateType   : u32
        stream.Write(_mate.ItemId);            // itemId         : u64
        stream.Write(_mate.UserState);         // userState      : u8
        stream.Write(_mate.Experience);        // exp            : i32, cumulative
        stream.Write(_mate.SpawnDelayTime);    // spawnDelayTime : u32

        var written = 0;
        foreach (var skill in _mate.Skills)
        {
            if (written >= SkillSlots)
                break;

            stream.Write(skill);
            written++;
        }

        for (var i = written; i < SkillSlots; i++)
            stream.Write(0u);

        return stream;
    }
}
