namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyEventTrigger
{
    public uint Id { get; init; }
    public uint CheckTime { get; init; }
    public uint EventRate { get; init; }
    public uint EventTime { get; init; }
    public uint TriggerType { get; init; }
    public string TriggerSubjectType { get; init; }
    public uint TriggerSubjectId { get; init; }
    public uint TriggerValue1 { get; init; }
    public uint TriggerValue2 { get; init; }
    public uint ZoneGroupId { get; init; }
}
