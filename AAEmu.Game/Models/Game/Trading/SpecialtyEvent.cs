namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyEvent
{
    public uint Id { get; init; }
    public uint EventType { get; init; }
    public string EventObjectType { get; init; }
    public uint EventObjectId { get; init; }
    public uint EventValue { get; init; }
    public uint TriggerId { get; init; }
    public string TooltipText { get; init; }
}
