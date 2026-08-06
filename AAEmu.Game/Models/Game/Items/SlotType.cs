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
    // Ships and land vehicles use their own equipment model. 0x07 is the short-lived
    // preliminary client container used while the equipment window is being prepared; it is
    // not valid in a committed CSChangeSlaveEquipment request. 0xF2 is the persistent
    // slave/vehicle equipment container used by the final model and server mutation.
    EquipmentSlavePreliminary = 0x07,
    EquipmentMateBattle = 0xED, // 237, MATE_TYPE_BATTLE
    EquipmentSlave = 0xF2,      // 242, ship / land-vehicle equipment
    EquipmentMate = 0xFC,       // 252, MATE_TYPE_RIDE
    System = 0xFF
}
