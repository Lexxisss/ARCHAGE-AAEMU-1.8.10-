using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Models.Tasks.Housing;

/// <summary>
/// Sends a building's state, and whatever is fixed to it, a moment after the building itself.
/// </summary>
/// <remarks>
/// Both messages are looked up against something the message before them creates: the state names
/// the building by the handle the unit message registered, and each door and window names the
/// building as its parent. Neither is queued behind that registration - the client answers the
/// callback it is given, and when the object is not there yet it does not complain or wait. The
/// state is dropped whole, silently, taking the owner, the name and the permissions with it; a
/// door with no parent to hang on is created loose instead.
///
/// Sending all three in one breath therefore only worked when the client happened to have finished
/// the first before it started the second. This gives it the gap.
/// </remarks>
public class HouseStateFollowUpTask : Task
{
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

        character.SendPacket(new SCHouseStatePacket(_house));
        _house.SendAttachedDoodads(character);
    }
}
