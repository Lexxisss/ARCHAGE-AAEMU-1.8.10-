namespace AAEmu.Game.Models.Game.Items;

public enum EquipmentItemSlot : byte
{
    Head = 0,
    Neck = 1,
    Chest = 2,
    Waist = 3,
    Legs = 4,
    Hands = 5,
    Feet = 6,
    Arms = 7,
    Back = 8,
    Ear1 = 9,
    Ear2 = 10,
    Finger1 = 11,
    Finger2 = 12,
    Undershirt = 13,
    Underpants = 14,
    Mainhand = 15,
    Offhand = 16,
    Ranged = 17,
    Musical = 18,
    // ---- somehow_special
    Face = 19,
    Hair = 20,
    Glasses = 21,
    Horns = 22,
    Tail = 23,
    Body = 24,
    Beard = 25,
    // ---- somehow_special
    Backpack = 26,
    Cosplay = 27,
    // These are AAEmu's persisted/server slot numbers, not the target wire
    // indices for the late range. Protocol1810EquipmentLayout maps them to
    // target slots 31/32/33 at serialization time. Standard slots 0..27 are
    // already identical to SCUnitState and must not be shifted.
    CosplayLooks = 28,
    Stabilizer = CosplayLooks,
    RaceCosplay = 29,
    RaceCosplayLooks = 30,
    // Persisted placeholders which map back to the target unnamed 28..30.
    ProtocolSlot31 = 31,
    ProtocolSlot32 = 32,
    ProtocolSlot33 = 33,
    ProtocolSlot34 = 34
}
