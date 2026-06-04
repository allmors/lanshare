using System.Text.Json.Serialization;

namespace LanShare.Models;

public sealed class DiscoveryProbe
{
    [JsonPropertyName("service")]
    public string Service { get; set; } = "LanShare";

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = "discover";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}
