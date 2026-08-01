using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// The recipes a player has pinned.
/// </summary>
/// <remarks>
/// The client keeps its own copy and expects the server to hold the real one: it is sent the whole
/// set once, then sends what to add and what to drop, and marks its own state pending until it
/// hears back. Nothing of this existed before - the pins vanished on logout because they were
/// never stored anywhere.
/// </remarks>
public class CharacterFavoriteCrafts
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Most recipes the client will send in one add or remove list.</summary>
    public const int MaxPerRequest = 30;

    private readonly HashSet<uint> _crafts = [];
    private bool _isDirty;

    public Character Owner { get; }

    public CharacterFavoriteCrafts(Character owner) => Owner = owner;

    public IReadOnlyCollection<uint> Crafts => _crafts;

    /// <summary>
    /// Applies one edit from the client and answers with the whole set.
    /// </summary>
    /// <remarks>
    /// The reply is the complete collection rather than a delta: it is the only message whose
    /// opcode is known, and sending everything cannot leave the two copies disagreeing. The
    /// dedicated acknowledgement the client waits on has no recovered opcode, so it is not sent -
    /// inventing one would be worse than the pending flag timing out.
    /// </remarks>
    public void Update(IReadOnlyList<uint> added, IReadOnlyList<uint> removed)
    {
        var changed = false;

        foreach (var craftId in added.Take(MaxPerRequest))
        {
            // A pin for a recipe that does not exist would come straight back to the client as
            // part of the set and confuse its own list.
            if (CraftManager.Instance.GetCraftById(craftId) == null)
            {
                Logger.Debug($"Favourite craft {craftId} does not exist, ignored for {Owner.Name}");
                continue;
            }

            changed |= _crafts.Add(craftId);
        }

        foreach (var craftId in removed.Take(MaxPerRequest))
            changed |= _crafts.Remove(craftId);

        if (changed)
            _isDirty = true;

        Send();
    }

    /// <summary>Sends the complete set, which is what the client initialises its list from.</summary>
    public void Send()
    {
        Owner.SendPacket(new SCFavoriteCraftsPacket([.. _crafts]));
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT craft_id FROM character_favorite_crafts WHERE `owner` = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            _crafts.Add(reader.GetUInt32("craft_id"));

        _isDirty = false;
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (!_isDirty)
            return;

        // Rewritten wholesale rather than diffed: the set is at most a few dozen numbers, and a
        // diff is one more thing that can drift out of step with what the player sees.
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM character_favorite_crafts WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        foreach (var craftId in _crafts)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO character_favorite_crafts (`owner`,`craft_id`) VALUES (@owner,@craft_id)";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Parameters.AddWithValue("@craft_id", craftId);
            command.Prepare();
            command.ExecuteNonQuery();
        }

        _isDirty = false;
    }
}
