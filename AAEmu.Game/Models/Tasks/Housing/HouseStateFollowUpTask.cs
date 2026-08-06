using System;
using System.Linq;

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
/// Doors and windows are deliberately not sent in the same execution turn. A second scheduled task
/// creates/registers them silently and publishes one batched record after this state has been applied.
/// </remarks>
public class HouseStateFollowUpTask : Task
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly House _house;
    private readonly uint _characterObjId;
    private readonly int _publicationVersion;

    public HouseStateFollowUpTask(House house, Character character, int publicationVersion)
    {
        _house = house;
        _characterObjId = character.ObjId;
        _publicationVersion = publicationVersion;
    }

    public override void Execute()
    {
        // The player may have walked away, logged out, or the building may be gone - in which case
        // the client has already been told to forget it and this would only confuse matters.
        var character = WorldManager.Instance.GetCharacterByObjId(_characterObjId);
        if (character == null || _house == null || _house.ObjId == 0 ||
            !_house.IsStatePublicationCurrent(_characterObjId, _publicationVersion))
            return;

        if (!WorldManager.GetAround<Character>(_house).Any(c => c.ObjId == _characterObjId))
        {
            _house.CompleteStatePublication(_characterObjId, _publicationVersion);
            return;
        }

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

        // Do not create/send fixtures in the same execution turn as the state. The target client
        // applies SCHouseState asynchronously; a child sent immediately afterwards can still see
        // no house-model and remains a visible but non-interactive orphan forever.
        TaskManager.Instance.Schedule(
            new HouseFixturesFollowUpTask(_house, character, _publicationVersion),
            TimeSpan.FromMilliseconds(500));
    }
}
