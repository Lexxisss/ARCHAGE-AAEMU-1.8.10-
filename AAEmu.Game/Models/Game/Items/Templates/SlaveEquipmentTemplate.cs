using System;

namespace AAEmu.Game.Models.Game.Items.Templates;

/// <summary>
/// Static client metadata for an item that occupies a ship/vehicle equipment slot.
/// </summary>
public class SlaveEquipmentTemplate : ItemTemplate
{
    public override Type ClassType => typeof(SlaveEquipmentItem);

    public float DoodadScale { get; set; }
    public uint DoodadId { get; set; }
    public uint RequireItemId { get; set; }
    public uint SlaveEquipKindId { get; set; }
    public uint SlaveEquipPackId { get; set; }
    public uint ChildSlaveId { get; set; }
    public uint SlotPackId { get; set; }
}
