using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BrowseResult>(cancellationToken: cancellationToken);
        return result ?? new BrowseResult();
    }

    public async Task DownloadFileAsync(
        DiscoveredServer server,
        BrowseEntry entry,
        string targetFolder,
        IProgress<TransferProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);

        var uri = $"{server.BaseAddress}/api/download?user=guest&path={Uri.EscapeDataString(entry.RelativePath)}";
        var filePath = Path.Combine(targetFolder, entry.Name);

        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(filePath);

        await CopyStreamWithProgressAsync(
            source,
            destination,
            totalBytes,
            bytesTransferred => progress?.Report(new TransferProgressInfo
            {
                Operation = "下载",
                FileName = entry.Name,
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                FileIndex = 1,
                FileCount = 1,
                IsCompleted = false
            }),
            cancellationToken);

        progress?.Report(new TransferProgressInfo
        {
            Operation = "下载",
            FileName = entry.Name,
            BytesTransferred = totalBytes ?? 0,
            TotalBytes = totalBytes,
            FileIndex = 1,
            FileCount = 1,
            IsCompleted = true
        });
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
            await using var fileStream = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
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
            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var uri = $"{server.BaseAddress}/api/upload?user=guest&path={Uri.EscapeDataString(targetRelativePath)}";
            using var request = CreateRequest(HttpMethod.Post, uri);
            request.Content = content;
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            progress?.Report(new TransferProgressInfo
            {
                Operation = "上传",
                FileName = Path.GetFileName(filePath),
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
        response.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();
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
            var buffer = new byte[_bufferSize];
            long totalSent = 0;

            while (true)
            {
                var bytesRead = await _sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (bytesRead <= 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalSent += bytesRead;
                _progressCallback(totalSent);
            }
        }
    }
}
