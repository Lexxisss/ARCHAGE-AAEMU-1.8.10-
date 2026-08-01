using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Managers.World;

public class StreamManager : Singleton<StreamManager>
{
    private readonly Dictionary<uint, ulong> _accounts;

    protected StreamManager()
    {
        _accounts = new Dictionary<uint, ulong>();
    }

    public static void Load()
    {
        // TODO ...
    }

    public void AddToken(ulong accountId, uint connectionId)
    {
        _accounts.Add(connectionId, accountId);
    }

    public void RemoveToken(uint token)
    {
        _accounts.Remove(token);
    }

    public void Login(StreamConnection connection, ulong accountId, uint token)
    {
        if (_accounts.ContainsKey(token))
        {
            if (accountId == _accounts[token])
            {
                var gCon = GameConnectionTable.Instance.GetConnection(token);
                connection.GameConnection = gCon;
                connection.SendPacket(new TCJoinResponsePacket(0));
            }
            else
            {
                _accounts.Remove(token);
                connection.SendPacket(new TCJoinResponsePacket(1));
            }
        }
        else
            connection.SendPacket(new TCJoinResponsePacket(1));
    }

    /// <summary>
    /// Whether an object belongs in the world's own cell listing.
    /// </summary>
    /// <remarks>
    /// This channel carries the world's static furniture. Anything a player put down has its own
    /// lifetime - it appears and disappears while people watch - and it already reaches the
    /// client the ordinary way, when somebody comes close enough to see it. Sending it here as
    /// well gives one object two arrivals and two owners of its state, and the state we name for
    /// a growing plant is not one its template necessarily has.
    /// </remarks>
    private static bool BelongsInCellStream(Doodad doodad)
    {
        return doodad.OwnerId == 0 && doodad.PlantTime == DateTime.MinValue;
    }

    public static void RequestCell(StreamConnection connection, uint instanceId, int x, int y)
    {
        if (connection is not null)
        {
            var worldId = connection.GameConnection?.ActiveChar?.Transform?.WorldId ?? WorldManager.DefaultWorldId;
            // TODO: Handle requests for instances correctly ?
            var doodads = WorldManager.Instance.GetInCell<Doodad>(worldId, x, y)
                .Where(BelongsInCellStream)
                .ToArray();
            var requestId = connection.GetNextRequestId(doodads);
            var count = Math.Min(doodads.Length, 30);
            var res = new Doodad[count];
            Array.Copy(doodads, 0, res, 0, count);
            connection.SendPacket(new TCDoodadStreamPacket(requestId, count, res));
        }
    }

    public static void ContinueCell(StreamConnection connection, int requestId, int next)
    {
        var doodads = connection.GetRequest(requestId);
        if (doodads == null)
            return;

        if (next < 0)
            next = 0;

        if (next >= doodads.Length)
        {
            connection.SendPacket(new TCDoodadStreamPacket(requestId, doodads.Length, Array.Empty<Doodad>()));
            connection.RemoveRequest(requestId);
            return;
        }

        var count = Math.Min(doodads.Length - next, 30);
        var res = new Doodad[count];
        Array.Copy(doodads, next, res, 0, count);
        next += count;
        connection.SendPacket(new TCDoodadStreamPacket(requestId, next, res));
    }
}
