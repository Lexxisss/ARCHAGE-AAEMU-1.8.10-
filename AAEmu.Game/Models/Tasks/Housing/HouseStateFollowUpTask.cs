using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Models.Tasks.Housing;

/// <summary>
/// Sends a building's state a moment after the building itself.
/// </summary>
/// <remarks>
/// The state names the building by the handle the unit message registered, and it is not queued
/// behind that registration - the client answers the callback it is given, and when the building is
/// not there yet it does not complain or wait. It drops the state whole and in silence, taking the
/// owner, the name and the permissions with it. Sending both in one breath only worked when the
/// client happened to finish the first before starting the second. This gives it the gap.
///
/// The doors and windows are not sent from here, and must not be: each announces itself when it is
/// put into the world, and sending them again on top of that is a second copy of a handle the
/// client already holds, which it throws away - taking the good one with it.
/// </remarks>
public class HouseStateFollowUpTask : Task
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly House _house;
    private readonly uint _characterObjId;

    public HouseStateFollowUpTask(House house, Character character)
    {
        _house = house;
        _characterObjId = character.ObjId;
    }

    public override void Execute()
    {
        // The player may have walked away, logged out, or the building may be gone - in which case
        // the client has already been told to forget it and this would only confuse matters.
        var character = WorldManager.Instance.GetCharacterByObjId(_characterObjId);
        if (character == null || _house == null || _house.ObjId == 0)
            return;

        // The four numbers ownership turns on, written down where they are certain to be reached.
        // They lived behind the tax request until now, which is exactly the path that stops working
        // when something is wrong.
        //
        // Two different comparisons are made of these, and they are not the same comparison:
        // whether this building is mine weighs the owner as sent against the identity of the living
        // unit, while putting furniture down weighs the owner resolved against the identity the
        // client cached for itself before any of this arrived. One passing while the other fails is
        // what we see, and only these side by side will say which pair disagrees.
        Logger.Info(
            "HouseOwnership: house={0} tl={1} objId={2} ownerId={3} ownerIdentity={4} " +
            "accountId={5} permission={6} viewer={7} viewerCharacterId={8} viewerAccountId={9}",
            _house.Id, _house.TlId, _house.ObjId, _house.OwnerId, _house.OwnerIdentity,
            _house.AccountId, _house.Permission, character.Name, character.Id, character.AccountId);

        character.SendPacket(new SCHouseStatePacket(_house));

        // Now that the building has a record at the other end, it may have its doors and windows.
        // Made any earlier they are fitted to nothing, and nothing fits them afterwards.
        _house.EnsureAttachedDoodads();
    }
}
