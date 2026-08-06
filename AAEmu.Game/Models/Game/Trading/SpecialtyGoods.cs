namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// One entry in SC SpecialtyGoods (0x0018).
/// </summary>
public sealed class SpecialtyGoods
{
    public uint ItemId { get; init; }
    public long CurrentAmount { get; init; }
    public long BaseAmount { get; init; }
    public uint Ratio { get; init; }
    public uint Stock { get; init; }
    public bool CanProduce { get; init; }
    public sbyte Currency { get; init; }
    public byte Grade { get; init; }
}
