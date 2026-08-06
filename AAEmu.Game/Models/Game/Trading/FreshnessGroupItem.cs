namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// One ordered freshness threshold from freshness_group_items.
/// reward_rate is per-mille (1150 = +15%); seller_share_ratio is percent
/// and is converted to per-mille by the dedicated server (value * 10).
/// </summary>
public sealed class FreshnessGroupItem
{
    public uint Id { get; init; }
    public uint FreshnessGroupId { get; init; }
    public uint RewardRate { get; init; }
    public uint SellerShareRatio { get; init; }
    public uint Time { get; init; }
    public string Tooltip { get; init; }
}
