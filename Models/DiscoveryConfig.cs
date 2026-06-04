namespace LanShare.Models;

public sealed class DiscoveryConfig
{
    public int DiscoveryPort { get; set; } = 49450;

    public int BroadcastIntervalSeconds { get; set; } = 5;

    public int DiscoveryTimeoutSeconds { get; set; } = 3;
}
