using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanShare.Models;

namespace LanShare.Services.Client;

public interface IFileShareClientService
{
    Task<BrowseResult> BrowseAsync(
        DiscoveredServer server,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        string targetFolder,
        IProgress<TransferProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);

    Task UploadFilesAsync(
        DiscoveredServer server,
        string targetRelativePath,
        IReadOnlyList<string> filePaths,
        IProgress<TransferProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);

    Task DeleteEntryAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        CancellationToken cancellationToken = default);

    Task CreateFolderAsync(
        DiscoveredServer server,
        string parentRelativePath,
        string folderName,
        CancellationToken cancellationToken = default);
}
