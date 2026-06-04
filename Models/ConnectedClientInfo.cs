using System;

namespace LanShare.Models;

public sealed class ConnectedClientInfo
{
    public string IpAddress { get; set; } = string.Empty;

    public string SystemName { get; set; } = string.Empty;

    public DateTime LastSeenLocalTime { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(SystemName)
        ? IpAddress
        : $"{SystemName} ({IpAddress})";
}
