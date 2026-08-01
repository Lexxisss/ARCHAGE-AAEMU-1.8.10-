namespace AAEmu.Game.Models.Game.Items;

public enum SlotType : byte
{
    None = 0,
    Equipment = 1,
    Inventory = 2,
    Bank = 3,
    Trade = 4,
    Mail = 5,
    // Equipment containers are not one kind. The client keeps a separate virtual container per
    // family and refuses anything outside this set before the change reaches its equipment
    // model, so the kind a mate's gear is announced under has to match the mate: a saddle sent
    // under the ride container never reaches a battle pet's slots.
    //
    // 0xF2 is the slave and vehicle container, and 0x07 a preliminary one; neither is modelled
    // here yet.
    EquipmentMateBattle = 0xED, // 237, MATE_TYPE_BATTLE
    EquipmentMate = 0xFC,       // 252, MATE_TYPE_RIDE
    System = 0xFF
}
