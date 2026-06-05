namespace LanShare.Models;

public sealed class ServerStartOptions
{
    public string ServerName { get; set; } = string.Empty;

    public string SharedFolderPath { get; set; } = string.Empty;

    public int ServicePort { get; set; }

    public int DiscoveryPort { get; set; }

    public int BroadcastIntervalSeconds { get; set; }

    public int MaxConcurrentDirectoryDownloads { get; set; } = 4;

    public int MaxConcurrentUploads { get; set; } = 8;

    public PermissionConfig Permissions { get; set; } = new();
}
