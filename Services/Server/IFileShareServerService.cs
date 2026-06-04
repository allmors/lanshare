using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Server;

public interface IFileShareServerService
{
    bool IsRunning { get; }

    string? BaseAddress { get; }

    Task StartAsync(ServerStartOptions options, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<BrowseResult> BrowseAsync(
        string userName,
        string relativePath,
        CancellationToken cancellationToken = default);

    bool HasPermission(string userName, string relativePath, FilePermission permission);

    IReadOnlyList<ConnectedClientInfo> GetRecentClientsSnapshot();
}
