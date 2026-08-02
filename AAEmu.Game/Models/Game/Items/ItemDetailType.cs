namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Discriminator for the detail block an item record carries. The client accepts 1 through 14
/// and rejects everything else outright, so nothing outside that range may go on the wire.
/// </summary>
/// <remarks>
/// Only the first eleven have names we can stand behind. Twelve through fourteen exist and have
/// exact sizes, but nothing in the client names them, and naming them after variants from another
/// version would be a guess dressed as a fact - so they are carried as opaque blocks of the right
/// length. See <see cref="Item.DetailPayloadLength"/>.
/// </remarks>
public enum ItemDetailType
{
    Invalid = 0,
    Equipment = 1,
    Slave = 2,
    Mate = 3,
    Ucc = 4,
    Treasure = 5,
    BigFish = 6,
    Decoration = 7,
    MusicSheet = 8,
    Glider = 9,
    SlaveEquipment = 10,
    Location = 11,
    Opaque12 = 12,
    Opaque13 = 13,
    Opaque14 = 14,
    TypeMax = 15,
}
