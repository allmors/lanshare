using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Discovery;

public sealed class UdpDiscoveryBroadcaster : IServiceDiscoveryBroadcaster
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private CancellationTokenSource? _broadcastCts;
    private Task? _broadcastTask;

    public bool IsBroadcasting => _broadcastTask is { IsCompleted: false };

    public Task StartAsync(ServerStartOptions options, CancellationToken cancellationToken = default)
    {
        _broadcastCts?.Cancel();
        _broadcastCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _broadcastCts.Token;

        _broadcastTask = Task.Run(async () =>
        {
            using var udpClient = CreateUdpClient(options.DiscoveryPort);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, options.DiscoveryPort);
            var nextBroadcastAt = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= nextBroadcastAt)
                {
                    await SendAnnouncementAsync(udpClient, broadcastEndpoint, options, token);
                    nextBroadcastAt = DateTime.UtcNow.AddSeconds(Math.Max(1, options.BroadcastIntervalSeconds));
                }

                var receiveTask = udpClient.ReceiveAsync(token).AsTask();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(250, token));
                if (completed != receiveTask)
                {
                    continue;
                }

                var result = await receiveTask;
                if (!IsDiscoveryProbe(result.Buffer))
                {
                    continue;
                }

                var replyEndpoint = new IPEndPoint(result.RemoteEndPoint.Address, result.RemoteEndPoint.Port);
                await SendAnnouncementAsync(udpClient, replyEndpoint, options, token);
            }
        }, token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_broadcastCts is null)
        {
            return;
        }

        _broadcastCts.Cancel();

        if (_broadcastTask is not null)
        {
            try
            {
                await _broadcastTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _broadcastCts.Dispose();
        _broadcastCts = null;
        _broadcastTask = null;
    }

    private static UdpClient CreateUdpClient(int port)
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.EnableBroadcast = true;
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return udpClient;
    }

    private static bool IsDiscoveryProbe(byte[] buffer)
    {
        try
        {
            var json = Encoding.UTF8.GetString(buffer);
            var probe = JsonSerializer.Deserialize<DiscoveryProbe>(json, SerializerOptions);
            return probe is not null &&
                   string.Equals(probe.Service, "LanShare", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(probe.MessageType, "discover", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task SendAnnouncementAsync(
        UdpClient udpClient,
        IPEndPoint endpoint,
        ServerStartOptions options,
        CancellationToken cancellationToken)
    {
        var announcement = new DiscoveryAnnouncement
        {
            Service = "LanShare",
            MessageType = "announcement",
            ServerName = string.IsNullOrWhiteSpace(options.ServerName) ? "LanShare" : options.ServerName,
            Version = "1.0",
            ServicePort = options.ServicePort
        };

        var json = JsonSerializer.Serialize(announcement, SerializerOptions);
        var payload = Encoding.UTF8.GetBytes(json);
        cancellationToken.ThrowIfCancellationRequested();
        await udpClient.SendAsync(payload, payload.Length, endpoint);
    }
}
