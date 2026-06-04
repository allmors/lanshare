namespace LanShare.Models;

public sealed class ClientConfig
{
    public string DownloadFolder { get; set; } = string.Empty;

    public string BuiltInServerHost { get; set; } = "192.168.202.163";

    public int BuiltInServerPort { get; set; } = 49443;

    public string PreferredServerAddress { get; set; } = string.Empty;

    public bool AutoConnectPreferredServerOnStartup { get; set; } = true;
}
