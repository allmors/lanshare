using LanShare.Infrastructure;

namespace LanShare.Models;

public sealed class DiscoveredServer : BindableBase
{
    private bool _isActive;

    public string ServerName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public int ServicePort { get; set; }

    public string? BaseAddressOverride { get; set; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string BaseAddress => !string.IsNullOrWhiteSpace(BaseAddressOverride)
        ? BaseAddressOverride!
        : $"http://{ResolveHost()}:{ServicePort}";

    public string DisplayName => ServerName;

    private string ResolveHost()
    {
        return string.IsNullOrWhiteSpace(IpAddress) ? "127.0.0.1" : IpAddress;
    }
}
