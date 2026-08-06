using System;

using AAEmu.Game.Models.Game.Units.Movements;

using NLog;

namespace AAEmu.Game.Utils.Logging;

/// <summary>
/// Records the movement bodies the server puts on the wire, in Logs/MoveDebug only.
/// The dedicated NLog rule is final, so these lines never reach the console or Server.log.
/// </summary>
/// <remarks>
/// Off until the movelog command turns it on. One walking NPC alone is ten bodies a second, and
/// the file is only useful while it is short enough to read: turn it on, do the one thing that is
/// being investigated, turn it off.
/// </remarks>
public static class MovementDebugLogger
{
    private static readonly Logger Logger = LogManager.GetLogger("MoveDebug");

    public static bool Enabled { get; set; }

    /// <summary>
    /// Writes one movement body: the fields as the server filled them in, and the bytes as they
    /// went out, so the two can be checked against each other.
    /// </summary>
    public static void UnitMovement(uint objId, MoveType type, byte[] body)
    {
        if (!Enabled || type == null)
            return;

        try
        {
            var unit = type as UnitMoveType;

            Logger.Info(
                "obj={0}|kind={1}|time={2}|flags=0x{3:X2}|actorFlags=0x{4:X4}|pos=({5:F2},{6:F2},{7:F2})|" +
                "vel=({8},{9},{10})|rot=({11},{12},{13})|delta=[{14},{15},{16}]|stance={17}|alertness={18}|body={19}",
                objId,
                type.Type,
                type.Time,
                type.Flags,
                unit?.ActorFlags ?? 0,
                type.X,
                type.Y,
                type.Z,
                type.VelX,
                type.VelY,
                type.VelZ,
                type.RotationX,
                type.RotationY,
                type.RotationZ,
                Delta(unit, 0),
                Delta(unit, 1),
                Delta(unit, 2),
                unit?.Stance ?? 0,
                unit?.Alertness ?? 0,
                body == null ? string.Empty : Convert.ToHexString(body));
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "obj={0}|movement debug snapshot failed", objId);
        }
    }

    private static int Delta(UnitMoveType unit, int index)
    {
        var delta = unit?.DeltaMovement;
        return delta != null && index < delta.Length ? delta[index] : 0;
    }
}
