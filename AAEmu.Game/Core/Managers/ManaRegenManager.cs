using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Charges mana for buffs that cost mana per tick rather than up front, currently Sprint.
/// The buff registers here when it starts and is dropped once the owner runs dry.
/// </summary>
public class ManaRegenManager : Singleton<ManaRegenManager>
{
    /// <summary>Matches the 200 ms tick of the Sprint buff.</summary>
    private const int UpdateDelay = 200;

    private static object Lock { get; } = new();
    private Dictionary<uint, ManaRegenTemplate> Registrations { get; set; }

    public void Initialize()
    {
        Registrations = new Dictionary<uint, ManaRegenTemplate>();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(UpdateDelay), true);
    }

    public void Register(Character player, ManaRegenTemplate template)
    {
        if (player == null || template == null)
            return;

        lock (Lock)
            Registrations[player.Id] = template;
    }

    public void UnRegister(Character player)
    {
        if (player == null)
            return;

        lock (Lock)
            Registrations.Remove(player.Id);
    }

    private void Tick(TimeSpan delta)
    {
        List<ManaRegenTemplate> due;
        lock (Lock)
        {
            if (Registrations == null || Registrations.Count == 0)
                return;

            // Snapshot: dropping the buff below unregisters the owner, and mutating the
            // dictionary while enumerating it would throw.
            due = Registrations.Values.ToList();
        }

        foreach (var entry in due)
        {
            if (entry.ConsumeTick())
                continue;

            UnRegister(entry.Owner);
            entry.Owner?.Buffs.RemoveBuff((uint)BuffConstants.Dash);
        }
    }
}
