namespace AAEmu.Game.Models.Game.DoodadObj;

public class DoodadFuncGroups
{
    public enum DoodadFuncGroupKind : uint
    {
        Start = 1,
        Normal = 2,
        End = 3
    }

    public uint Id { get; set; }
    public uint Almighty { get; set; }
    public DoodadFuncGroupKind GroupKindId { get; set; }
    public uint SoundId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string PhaseMessage { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint SoundTime { get; set; }
    public bool IsMessageToZone { get; set; }
    public uint MessageFactionId { get; set; }
    public bool IsMessageToWorld { get; set; }
    public bool UseUiMessage { get; set; }
    public string IconKey { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string TitleMessage { get; set; } = string.Empty;
    public string TitleColor { get; set; } = string.Empty;
    public int OverHeadMarkGap { get; set; }
}
