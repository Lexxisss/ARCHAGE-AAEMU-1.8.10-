using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class CharacterIdManager : IdManager
{
    private static CharacterIdManager _instance;

    /// <summary>
    /// The lowest id a character may be given.
    /// </summary>
    /// <remarks>
    /// These numbers are not private to the database: they go out as the identity of whoever owns
    /// a building, and the client only takes one at face value from a thousand upwards. Below that
    /// the range is its own: six hundred means the owner is the public, six hundred and one means
    /// look the owner up through another object, and everything else resolves to nobody at all.
    ///
    /// Numbering from one put every character in that dead range, so a building had an owner in
    /// our database and none on screen, and the checks that ask whether this player owns this
    /// place all answered no.
    ///
    /// Characters already numbered below this keep their numbers - the manager passes over them
    /// with a warning rather than handing them out again - and they keep the problem with them.
    /// </remarks>
    private const uint FirstId = 1000;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = System.Array.Empty<uint>();
    private static readonly string[,] ObjTables = { { "characters", "id" }, { "slaves", "id" } };

    public static CharacterIdManager Instance => _instance ?? (_instance = new CharacterIdManager());

    public CharacterIdManager() : base("CharacterIdManager", FirstId, LastId, ObjTables, Exclude)
    {
    }
}
