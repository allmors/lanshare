namespace LanShare.Models;

public sealed class ServerConfig
{
    public string ServerName { get; set; } = "局域网共享-服务端";

    public string SharedFolderPath { get; set; } = string.Empty;

    public int ServicePort { get; set; } = 49443;

    public int MaxConcurrentDirectoryDownloads { get; set; } = 4;

    public int MaxConcurrentUploads { get; set; } = 8;
}
