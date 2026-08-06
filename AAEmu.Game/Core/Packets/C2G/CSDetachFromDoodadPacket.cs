using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDetachFromDoodadPacket : GamePacket
{
    public CSDetachFromDoodadPacket() : base(CSOffsets.CSDetachFromDoodadPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterObjId = stream.ReadBc();
        var doodadObjId = stream.ReadBc();

        // Target 1.8.1.0 appends seven reserved bytes. Captures seen so far contain zeros.
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);

        var character = Connection.ActiveChar;
        if (character.ObjId != characterObjId || character.Bonding == null || character.Bonding.ObjId != doodadObjId)
            return;

        var bond = character.Bonding;
        var doodad = bond.GetOwner();
        if (doodad == null)
            return;

        var beforeWorld = character.Transform.World.Clone();
        var beforeLocal = character.Transform.Local.Clone();
        var beforeParentObjId = character.Transform.Parent?.GameObject?.ObjId ?? 0;
        var beforeStickyObjId = character.Transform.StickyParent?.GameObject?.ObjId ?? 0;

        // VehicleSeat owns the transform relationship. For a static doodad it leaves Parent null;
        // for a doodad mounted on a transfer it detaches from the transfer while preserving world
        // coordinates. Do not rebuild a world transform manually here: SetParent already converts
        // parent-local coordinates back to world coordinates during detachment.
        doodad.Seat.UnLoadPassenger(character, doodad.ObjId);

        // Defensive cleanup for malformed/legacy in-memory bonds. The normal path above clears both.
        if (character.Transform.StickyParent != null)
            character.Transform.StickyParent = null;
        if (character.Transform.Parent != null)
            character.Transform.Parent = null;

        bond.SetOwner(null);
        character.Bonding = null;
        character.Transform.ResetFinalizeTransform();

        // The middle UInt64 is the character identity stored in the client Unit object and placed
        // into the doodad's occupant-token array by SCAttachToDoodad. It is Character.Id, not the
        // animation action id. Sending the animation id leaves the token in the client and keeps
        // every later doodad action locally blocked.
        var characterIdentity = (ulong)character.Id;
        character.BroadcastPacket(
            new SCUnbondDoodadPacket(character.ObjId, characterIdentity, doodadObjId),
            true);

        var afterWorld = character.Transform.World;
        Logger.Debug(
            "Doodad detach: char={0}/{1}, doodad={2}, token={3}, parent={4}, sticky={5}, " +
            "beforeWorld=({6:F2},{7:F2},{8:F2}), beforeLocal=({9:F2},{10:F2},{11:F2}), " +
            "afterWorld=({12:F2},{13:F2},{14:F2})",
            character.Id,
            character.ObjId,
            doodadObjId,
            characterIdentity,
            beforeParentObjId,
            beforeStickyObjId,
            beforeWorld.Position.X,
            beforeWorld.Position.Y,
            beforeWorld.Position.Z,
            beforeLocal.Position.X,
            beforeLocal.Position.Y,
            beforeLocal.Position.Z,
            afterWorld.Position.X,
            afterWorld.Position.Y,
            afterWorld.Position.Z);
    }
}
