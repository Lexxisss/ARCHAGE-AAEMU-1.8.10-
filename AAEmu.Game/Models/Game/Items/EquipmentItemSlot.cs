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
    CosplayLooks = 28,
    // Legacy AAEmu data currently places equip_pack_cloths.stabilizer_id in
    // this compact slot. The target script enum names bit 28 COSPLAYLOOKS.
    Stabilizer = CosplayLooks,
    RaceCosplay = 29,
    RaceCosplayLooks = 30,
    // The target equipment serializer iterates four additional internal bits,
    // but the target binary exposes no public ES_* names for them.
    ProtocolSlot31 = 31,
    ProtocolSlot32 = 32,
    ProtocolSlot33 = 33,
    ProtocolSlot34 = 34
}
