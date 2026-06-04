using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Discovery;

public interface IServiceDiscoveryBroadcaster
{
    bool IsBroadcasting { get; }

    Task StartAsync(ServerStartOptions options, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
