using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LanShare.Models;

namespace LanShare.Services.Client;

public sealed class FileShareClientService : IFileShareClientService
{
    private const int BufferSize = 1024 * 128;
    private const string ClientNameHeader = "X-LanShare-ClientName";

    private readonly HttpClient _httpClient = new(new HttpClientHandler())
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task<BrowseResult> BrowseAsync(
        DiscoveredServer server,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var uri = $"{server.BaseAddress}/api/browse?user=guest&path={Uri.EscapeDataString(relativePath)}";
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeWithDetailsAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<BrowseResult>(cancellationToken: cancellationToken);
        return result ?? new BrowseResult();
    }

    public async Task DownloadEntryAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        string targetFolder,
        IProgress<TransferProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);

        if (entry.IsDirectory)
        {
            await DownloadDirectoryAsync(server, entry, targetFolder, progress, cancellationToken);
            return;
        }

        var filePath = Path.Combine(targetFolder, entry.Name);
        await DownloadFileToPathAsync(
            server,
            entry,
            filePath,
            progress,
            fileIndex: 1,
            fileCount: 1,
            totalBytesOverride: entry.Size > 0 ? entry.Size : null,
            aggregateBytesCompleted: 0,
            cancellationToken);
    }

    public async Task UploadFilesAsync(
        DiscoveredServer server,
        string targetRelativePath,
        IReadOnlyList<string> filePaths,
        IProgress<TransferProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < filePaths.Count; index++)
        {
            var filePath = filePaths[index];
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var streamContent = new ProgressStreamContent(
                fileStream,
                BufferSize,
                bytesSent => progress?.Report(new TransferProgressInfo
                {
                    Operation = "上传",
                    FileName = Path.GetFileName(filePath),
                    BytesTransferred = bytesSent,
                    TotalBytes = fileStream.Length,
                    FileIndex = index + 1,
                    FileCount = filePaths.Count,
                    IsCompleted = false
                }));

            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var fileName = Path.GetFileName(filePath);
            var uri =
                $"{server.BaseAddress}/api/upload-file?user=guest&path={Uri.EscapeDataString(targetRelativePath)}&name={Uri.EscapeDataString(fileName)}";
            using var request = CreateRequest(HttpMethod.Post, uri);
            request.Content = streamContent;
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            await EnsureSuccessStatusCodeWithDetailsAsync(response, cancellationToken);

            progress?.Report(new TransferProgressInfo
            {
                Operation = "上传",
                FileName = fileName,
                BytesTransferred = fileStream.Length,
                TotalBytes = fileStream.Length,
                FileIndex = index + 1,
                FileCount = filePaths.Count,
                IsCompleted = true
            });
        }
    }

    public async Task DeleteEntryAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        CancellationToken cancellationToken = default)
    {
        var uri = $"{server.BaseAddress}/api/entry?user=guest&path={Uri.EscapeDataString(entry.RelativePath)}";
        using var request = CreateRequest(HttpMethod.Delete, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeWithDetailsAsync(response, cancellationToken);
    }

    public async Task CreateFolderAsync(
        DiscoveredServer server,
        string parentRelativePath,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        var uri =
            $"{server.BaseAddress}/api/folder?user=guest&path={Uri.EscapeDataString(parentRelativePath)}&name={Uri.EscapeDataString(folderName)}";
        using var request = CreateRequest(HttpMethod.Post, uri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeWithDetailsAsync(response, cancellationToken);
    }

    private async Task DownloadDirectoryAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        string targetFolder,
        IProgress<TransferProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var targetRoot = GetUniqueDirectoryPath(targetFolder, entry.Name);
        var plan = await BuildDirectoryDownloadPlanAsync(server, entry, cancellationToken);

        Directory.CreateDirectory(targetRoot);
        foreach (var directoryRelativePath in plan.DirectoryRelativePaths)
        {
            Directory.CreateDirectory(Path.Combine(targetRoot, ConvertToLocalPath(directoryRelativePath)));
        }

        if (plan.Files.Count == 0)
        {
            progress?.Report(new TransferProgressInfo
            {
                Operation = "下载",
                FileName = entry.Name,
                BytesTransferred = 0,
                TotalBytes = 0,
                OverallBytesTransferred = 0,
                OverallTotalBytes = 0,
                FileIndex = 0,
                FileCount = 0,
                IsCompleted = true
            });
            return;
        }

        long aggregateBytesCompleted = 0;
        for (var index = 0; index < plan.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = plan.Files[index];
            var destinationPath = Path.Combine(targetRoot, ConvertToLocalPath(file.RelativePathUnderRoot));
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await DownloadFileToPathAsync(
                server,
                file.Entry,
                destinationPath,
                progress,
                index + 1,
                plan.Files.Count,
                plan.TotalBytes,
                aggregateBytesCompleted,
                cancellationToken);

            aggregateBytesCompleted += file.Entry.Size;
        }
    }

    private async Task<RemoteDirectoryDownloadPlan> BuildDirectoryDownloadPlanAsync(
        DiscoveredServer server,
        BrowseEntry rootEntry,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        var files = new List<RemoteFileDownloadItem>();

        await CollectDirectoryPlanAsync(server, rootEntry.RelativePath, string.Empty, directories, files, cancellationToken);

        return new RemoteDirectoryDownloadPlan(
            directories,
            files,
            files.Sum(item => item.Entry.Size));
    }

    private async Task CollectDirectoryPlanAsync(
        DiscoveredServer server,
        string remoteRelativePath,
        string relativePathUnderRoot,
        ICollection<string> directories,
        ICollection<RemoteFileDownloadItem> files,
        CancellationToken cancellationToken)
    {
        var result = await BrowseAsync(server, remoteRelativePath, cancellationToken);
        foreach (var child in result.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childRelativePathUnderRoot = string.IsNullOrWhiteSpace(relativePathUnderRoot)
                ? child.Name
                : $"{relativePathUnderRoot}/{child.Name}";

            if (child.IsDirectory)
            {
                directories.Add(childRelativePathUnderRoot);
                await CollectDirectoryPlanAsync(
                    server,
                    child.RelativePath,
                    childRelativePathUnderRoot,
                    directories,
                    files,
                    cancellationToken);
                continue;
            }

            files.Add(new RemoteFileDownloadItem(child, childRelativePathUnderRoot));
        }
    }

    private async Task DownloadFileToPathAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        string filePath,
        IProgress<TransferProgressInfo>? progress,
        int fileIndex,
        int fileCount,
        long? totalBytesOverride,
        long aggregateBytesCompleted,
        CancellationToken cancellationToken)
    {
        var uri = $"{server.BaseAddress}/api/download?user=guest&path={Uri.EscapeDataString(entry.RelativePath)}";
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessStatusCodeWithDetailsAsync(response, cancellationToken);

        var fileTotalBytes = response.Content.Headers.ContentLength ?? (entry.Size > 0 ? entry.Size : 0);
        var overallTotalBytes = totalBytesOverride ?? response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await CopyStreamWithProgressAsync(
            source,
            destination,
            fileTotalBytes,
            bytesTransferred => progress?.Report(new TransferProgressInfo
            {
                Operation = "下载",
                FileName = entry.Name,
                BytesTransferred = bytesTransferred,
                TotalBytes = fileTotalBytes,
                OverallBytesTransferred = aggregateBytesCompleted + bytesTransferred,
                OverallTotalBytes = overallTotalBytes,
                FileIndex = fileIndex,
                FileCount = fileCount,
                IsCompleted = false
            }),
            cancellationToken);

        progress?.Report(new TransferProgressInfo
        {
            Operation = "下载",
            FileName = entry.Name,
            BytesTransferred = fileTotalBytes,
            TotalBytes = fileTotalBytes,
            OverallBytesTransferred = aggregateBytesCompleted + fileTotalBytes,
            OverallTotalBytes = overallTotalBytes,
            FileIndex = fileIndex,
            FileCount = fileCount,
            IsCompleted = true
        });
    }

    private static async Task EnsureSuccessStatusCodeWithDetailsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("detail", out var detailElement))
                {
                    var detail = detailElement.GetString();
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        throw new HttpRequestException(MapFriendlyErrorMessage(response.StatusCode, detail), null, response.StatusCode);
                    }
                }
            }
            catch (JsonException)
            {
            }

            throw new HttpRequestException(MapFriendlyErrorMessage(response.StatusCode, body), null, response.StatusCode);
        }

        throw new HttpRequestException(
            MapFriendlyErrorMessage(response.StatusCode, response.ReasonPhrase),
            null,
            response.StatusCode);
    }

    private static string MapFriendlyErrorMessage(HttpStatusCode? statusCode, string? detail)
    {
        var message = detail?.Trim();
        if (statusCode == HttpStatusCode.Conflict)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "当前目录正在被其他操作占用，请稍后重试。";
            }

            return message.Contains("目录", StringComparison.OrdinalIgnoreCase)
                ? message
                : $"操作冲突：{message}";
        }

        return string.IsNullOrWhiteSpace(message)
            ? $"请求失败：{(int?)statusCode ?? 0}"
            : message;
    }

    private static string GetUniqueDirectoryPath(string targetFolder, string directoryName)
    {
        var candidate = Path.Combine(targetFolder, directoryName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 1; index < 1000; index++)
        {
            candidate = Path.Combine(targetFolder, $"{directoryName} ({index})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"无法为目录 '{directoryName}' 找到可用的下载位置。");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation(ClientNameHeader, Environment.MachineName);
        return request;
    }

    private static async Task CopyStreamWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        Action<long> reportProgress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long bytesTransferred = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesTransferred += bytesRead;
            reportProgress(bytesTransferred);
        }
    }

    private static string ConvertToLocalPath(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _sourceStream;
        private readonly int _bufferSize;
        private readonly Action<long> _progressCallback;

        public ProgressStreamContent(Stream sourceStream, int bufferSize, Action<long> progressCallback)
        {
            _sourceStream = sourceStream;
            _bufferSize = bufferSize;
            _progressCallback = progressCallback;
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_sourceStream.CanSeek)
            {
                length = _sourceStream.Length;
                return true;
            }

            length = -1;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[_bufferSize];
            long totalSent = 0;

            while (true)
            {
                var bytesRead = await _sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalSent += bytesRead;
                _progressCallback(totalSent);
            }
        }
    }

    private sealed record RemoteFileDownloadItem(BrowseEntry Entry, string RelativePathUnderRoot);

    private sealed record RemoteDirectoryDownloadPlan(
        IReadOnlyList<string> DirectoryRelativePaths,
        IReadOnlyList<RemoteFileDownloadItem> Files,
        long TotalBytes);
}
