using System.Text.Json.Serialization;

namespace LanShare.Models;

public sealed class DiscoveryAnnouncement
{
    [JsonPropertyName("service")]
    public string Service { get; set; } = "LanShare";

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "announcement";

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = "LanShare";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("port")]
    public int ServicePort { get; set; } = 49443;
}
