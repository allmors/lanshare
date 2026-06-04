using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Discovery;

public sealed class UdpDiscoveryClient : IServiceDiscoveryClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(
        DiscoveryConfig config,
        CancellationToken cancellationToken = default)
    {
        using var udpClient = CreateListener(config.DiscoveryPort);
        await SendProbeAsync(udpClient, config.DiscoveryPort, cancellationToken);

        var servers = new Dictionary<string, DiscoveredServer>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, config.DiscoveryTimeoutSeconds));

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var receiveTask = udpClient.ReceiveAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(remaining, cancellationToken));
            if (completed != receiveTask)
            {
                break;
            }

            var result = await receiveTask;
            var announcement = TryParseAnnouncement(result.Buffer);
            if (announcement is null)
            {
                continue;
            }

            var ipAddress = result.RemoteEndPoint.Address.ToString();
            var key = $"{ipAddress}:{announcement.ServicePort}";

            servers[key] = new DiscoveredServer
            {
                ServerName = string.IsNullOrWhiteSpace(announcement.ServerName) ? announcement.Service : announcement.ServerName,
                IpAddress = ipAddress,
                Version = announcement.Version,
                ServicePort = announcement.ServicePort
            };
        }

        return servers.Values
            .OrderBy(item => item.ServerName)
            .ThenBy(item => item.IpAddress)
            .ToArray();
    }

    private static DiscoveryAnnouncement? TryParseAnnouncement(byte[] buffer)
    {
        try
        {
            var json = Encoding.UTF8.GetString(buffer);
            var announcement = JsonSerializer.Deserialize<DiscoveryAnnouncement>(json, SerializerOptions);
            if (announcement is null)
            {
                return null;
            }

            if (!string.Equals(announcement.Service, "LanShare", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(announcement.MessageType, "announcement", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return announcement;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SendProbeAsync(UdpClient udpClient, int discoveryPort, CancellationToken cancellationToken)
    {
        var probe = new DiscoveryProbe();
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(probe, SerializerOptions));
        var endpoint = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
        cancellationToken.ThrowIfCancellationRequested();
        await udpClient.SendAsync(payload, payload.Length, endpoint);
    }

    private static UdpClient CreateListener(int port)
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.EnableBroadcast = true;
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return udpClient;
    }
}
