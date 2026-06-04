namespace LanShare.Models;

public sealed class AppConfig
{
    public AppMode StartupMode { get; set; } = AppMode.Server;

    public string AdminAccessKey { get; set; } = "LanShareAdmin";

    public ServerConfig Server { get; set; } = new();

    public DiscoveryConfig Discovery { get; set; } = new();

    public ClientConfig Client { get; set; } = new();

    public PermissionConfig Permissions { get; set; } = new();
}
