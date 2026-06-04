using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Discovery;

public interface IServiceDiscoveryClient
{
    Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(
        DiscoveryConfig config,
        CancellationToken cancellationToken = default);
}
