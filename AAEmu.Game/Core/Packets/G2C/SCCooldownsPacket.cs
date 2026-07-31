using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Authoritative cooldown snapshot (SC 0x0255), verified against the target x2game.dll:
/// constructor 0x3933F310, vtable 0x39D59310, body serializer 0x399CDE50.
///
/// Three categories, each a count followed by that many triples. The client keeps its own
/// cooldown maps and never showed a cooldown sweep because this snapshot was never
/// implemented - only the opcode constant existed, and the one call site was commented out.
/// </summary>
/// <remarks>
/// The serializer labels all three integers of a triple identically (`type`), so only the
/// first is known to be the map key. The other two are deliberately left unnamed rather
/// than guessed as start/end/remaining.
/// </remarks>
public class SCCooldownsPacket : GamePacket
{
    /// <summary>Each category reserves 150 entries in the packet object.</summary>
    private const int MaxEntriesPerCategory = 150;

    public readonly record struct CooldownEntry(uint Id, uint ValueA, uint ValueB);

    private readonly List<CooldownEntry> _skills;
    private readonly List<CooldownEntry> _tags;
    private readonly List<CooldownEntry> _charges;

    public SCCooldownsPacket(
        List<CooldownEntry> skills = null,
        List<CooldownEntry> tags = null,
        List<CooldownEntry> charges = null)
        : base(SCOffsets.SCCooldownsPacket, 5)
    {
        _skills = skills ?? [];
        _tags = tags ?? [];
        _charges = charges ?? [];
    }

    public static SCCooldownsPacket ForCharacter(Character character)
    {
        var skills = new List<CooldownEntry>();
        if (character != null)
        {
            var now = DateTime.UtcNow;
            foreach (var (skillId, endTime) in character.Cooldowns.Cooldowns)
            {
                var remaining = endTime - now;
                if (remaining <= TimeSpan.Zero)
                    continue;

                var remainingMs = (uint)Math.Clamp(remaining.TotalMilliseconds, 0, uint.MaxValue);
                skills.Add(new CooldownEntry(skillId, remainingMs, remainingMs));
                if (skills.Count >= MaxEntriesPerCategory)
                    break;
            }
        }

        return new SCCooldownsPacket(skills);
    }

    public override PacketStream Write(PacketStream stream)
    {
        WriteCategory(stream, _skills);
        WriteCategory(stream, _tags);
        WriteCategory(stream, _charges);
        return stream;
    }

    private static void WriteCategory(PacketStream stream, List<CooldownEntry> entries)
    {
        // The serializer shows no clamp of its own before the loop, so the count must never
        // exceed what the packet object reserves.
        var count = Math.Min(entries.Count, MaxEntriesPerCategory);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(entries[i].Id);
            stream.Write(entries[i].ValueA);
            stream.Write(entries[i].ValueB);
        }
    }
}
