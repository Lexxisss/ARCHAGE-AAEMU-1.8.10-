using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Models.Tasks.Housing;

/// <summary>Publishes built-in house fixtures after the house-model exists on the client.</summary>
public class HouseFixturesFollowUpTask : Task
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly House _house;
    private readonly uint _characterObjId;
    private readonly int _publicationVersion;

    public HouseFixturesFollowUpTask(House house, Character character, int publicationVersion)
    {
        _house = house;
        _characterObjId = character.ObjId;
        _publicationVersion = publicationVersion;
    }

    public override void Execute()
    {
        var character = WorldManager.Instance.GetCharacterByObjId(_characterObjId);
        if (character == null)
        {
            Logger.Debug("House fixtures skipped: viewer objId={0} no longer exists", _characterObjId);
            return;
        }

        if (_house == null || _house.ObjId == 0)
        {
            Logger.Debug("House fixtures skipped: house no longer exists for viewer={0}", character.Name);
            return;
        }

        if (!_house.IsStatePublicationCurrent(_characterObjId, _publicationVersion))
        {
            Logger.Debug("House fixtures skipped as stale: house={0}, viewer={1}, version={2}",
                _house.Id, character.Name, _publicationVersion);
            return;
        }

        if (!WorldManager.GetAround<Character>(_house).Any(c => c.ObjId == _characterObjId))
        {
            Logger.Debug("House fixtures skipped: viewer left range, house={0}, viewer={1}",
                _house.Id, character.Name);
            _house.CompleteStatePublication(_characterObjId, _publicationVersion);
            return;
        }

        if (_house.CurrentStep == -1)
        {
            Logger.Info("House fixture publication begin: house={0}, objId={1}, viewer={2}, existing={3}",
                _house.Id, _house.ObjId, character.Name, _house.AttachedDoodads.Count);
            _house.EnsureAttachedDoodads();
            _house.SendAttachedDoodads(character);
            Logger.Info("House fixture publication complete: house={0}, viewer={1}, count={2}",
                _house.Id, character.Name, _house.AttachedDoodads.Count);
        }
        else
        {
            Logger.Debug("House fixtures not published: house={0} is still at step={1}",
                _house.Id, _house.CurrentStep);
        }

        _house.CompleteStatePublication(_characterObjId, _publicationVersion);
    }
}
