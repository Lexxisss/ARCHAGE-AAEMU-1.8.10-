namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 ItemAction discriminator used by SCItemTaskSuccessPacket.
/// Values 8 and above differ from older AAEmu protocol branches because this
/// client has a dedicated store-remove action at index 8.
/// </summary>
public enum ItemAction
{
    Invalid = 0,
    ChangeMoneyAmount = 1,
    ChangeBankMoneyAmount = 2,
    ChangeGamePoint = 3,
    AddStack = 4,
    Create = 5,
    Take = 6,
    Remove = 7,
    StoreRemove = 8,
    SwapSlot = 9,
    UpdateDetail = 10,
    SetFlagsBits = 11,
    UpdateFlags = 12,
    Seize = 13,
    RemoveCrafting = 14,
    ChangeGrade = 15,
    ChangeOwner = 16,
    ChangeAaPoint = 17,
    ChangeBankAaPoint = 18,
    ChangeAutoUseAaPoint = 19,
    UpdateChargeUseSkillTime = 20,
    ButlerSwap = 21,
}
