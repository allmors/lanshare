using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using LanShare.Models;
using LanShare.Services.Permissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace LanShare.Services.Server;

public sealed class FileShareServerService : IFileShareServerService
{
    private const string ClientNameHeader = "X-LanShare-ClientName";
    private const int StreamCopyBufferSize = 1024 * 1024;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathSemaphores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConnectedClientInfo> _recentClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _activeDirectoryDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPermissionService _permissionService;
    private readonly string _diagnosticLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LanShare.Server",
        "logs",
        "directory-download.log");
    private WebApplication? _webApplication;
    private string? _sharedFolderPath;
    private PermissionConfig _permissionConfig = new();
    private SemaphoreSlim _directoryDownloadLimiter = new(4, 4);
    private SemaphoreSlim _uploadLimiter = new(8, 8);

    public FileShareServerService()
        : this(new PermissionService())
    {
    }

    public FileShareServerService(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public bool IsRunning => _webApplication is not null;

    public string? BaseAddress { get; private set; }

    public async Task StartAsync(ServerStartOptions options, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        var maxConcurrentDirectoryDownloads = Math.Max(1, options.MaxConcurrentDirectoryDownloads);
        var maxConcurrentUploads = Math.Max(1, options.MaxConcurrentUploads);
        _directoryDownloadLimiter = new SemaphoreSlim(maxConcurrentDirectoryDownloads, maxConcurrentDirectoryDownloads);
        _uploadLimiter = new SemaphoreSlim(maxConcurrentUploads, maxConcurrentUploads);
        LogDirectoryDownloadTrace("服务端启动", $"sharedFolder={options.SharedFolderPath}, servicePort={options.ServicePort}, discoveryPort={options.DiscoveryPort}");

        _sharedFolderPath = options.SharedFolderPath;
        _permissionConfig = options.Permissions ?? new PermissionConfig();
        BaseAddress = $"http://0.0.0.0:{options.ServicePort}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BaseAddress);
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Limits.MaxRequestBodySize = null;
            kestrelOptions.Limits.MinRequestBodyDataRate = null;
            kestrelOptions.AllowSynchronousIO = true;
        });
        builder.Services.Configure<FormOptions>(formOptions =>
        {
            formOptions.MultipartBodyLengthLimit = long.MaxValue;
            formOptions.MultipartHeadersLengthLimit = int.MaxValue;
            formOptions.ValueLengthLimit = int.MaxValue;
            formOptions.MemoryBufferThreshold = 1024 * 1024;
        });

        var app = builder.Build();

        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/browse", (HttpContext context, string? user, string? path) =>
        {
            TrackClient(context);
            var effectiveUser = GetEffectiveUser(user);
            var relativePath = path ?? string.Empty;
            if (!HasPermission(effectiveUser, relativePath, FilePermission.Read))
            {
                return Results.Forbid();
            }

            var result = BrowseInternal(effectiveUser, relativePath);
            return Results.Ok(result);
        });

        app.MapGet("/api/download", (HttpContext context, string? user, string path) =>
        {
            TrackClient(context);
            var effectiveUser = GetEffectiveUser(user);
            if (!HasPermission(effectiveUser, path, FilePermission.Read))
            {
                return Results.Forbid();
            }

            var fullPath = ResolvePath(path);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return Results.NotFound();
            }

            var contentType = _contentTypeProvider.TryGetContentType(fullPath, out var detected)
                ? detected
                : "application/octet-stream";

            return Results.File(fullPath, contentType, fileDownloadName: Path.GetFileName(fullPath), enableRangeProcessing: true);
        });

        app.MapGet("/api/download-directory", async (HttpContext context, string? user, string path, CancellationToken token) =>
        {
            try
            {
                LogDirectoryDownloadTrace("收到目录下载请求", $"原始 path={path}, user={user}, remoteIp={context.Connection.RemoteIpAddress}");
                TrackClient(context);
                var effectiveUser = GetEffectiveUser(user);
                var relativePath = NormalizeRelativePath(path);
                LogDirectoryDownloadTrace("目录下载请求已规范化", $"relativePath={relativePath}, effectiveUser={effectiveUser}");

                var hasReadPermission = HasPermission(effectiveUser, relativePath, FilePermission.Read);
                LogDirectoryDownloadTrace("目录下载权限检查完成", $"relativePath={relativePath}, effectiveUser={effectiveUser}, hasReadPermission={hasReadPermission}");
                if (!hasReadPermission)
                {
                    LogDirectoryDownloadTrace("目录下载请求被拒绝", $"relativePath={relativePath}, effectiveUser={effectiveUser}");
                    return Results.Forbid();
                }

                var fullPath = ResolvePath(relativePath);
                LogDirectoryDownloadTrace("目录下载路径解析完成", $"relativePath={relativePath}, resolvedPath={fullPath}");
                if (fullPath is null || !Directory.Exists(fullPath))
                {
                    LogDirectoryDownloadTrace("目录下载目标不存在", $"relativePath={relativePath}, resolvedPath={fullPath}");
                    return Results.NotFound();
                }

                context.Response.ContentType = "application/zip";
                var archiveFileName = $"{Path.GetFileName(fullPath)}.zip";
                var contentDisposition = new ContentDispositionHeaderValue("attachment");
                contentDisposition.FileName = "download.zip";
                contentDisposition.FileNameStar = archiveFileName;
                context.Response.Headers.ContentDisposition = contentDisposition.ToString();
                LogDirectoryDownloadTrace("开始打包目录", $"relativePath={relativePath}, fullPath={fullPath}");

                using var downloadLease = await AcquireDirectoryDownloadLeaseAsync(relativePath, token);
                using var archive = new ZipArchive(context.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
                await WriteDirectoryToArchiveAsync(archive, effectiveUser, fullPath, token);
                LogDirectoryDownloadTrace("目录打包完成", $"relativePath={relativePath}, fullPath={fullPath}");
                return Results.Empty;
            }
            catch (DirectoryDownloadException ex)
            {
                LogDirectoryDownloadFailure(NormalizeRelativePath(path), ex.FailedPath, ex);
                return Results.Problem(
                    detail: $"目录下载失败，文件无法读取：{ex.FailedPath}。{ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Directory download failed");
            }
            catch (Exception ex)
            {
                LogDirectoryDownloadFailure(NormalizeRelativePath(path), path, ex);
                return Results.Problem(
                    detail: $"目录下载失败：{path}。{ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Directory download failed");
            }
        });

        app.MapPost("/api/upload", async (HttpRequest request, string? user, string? path, CancellationToken token) =>
        {
            TrackClient(request.HttpContext);
            var effectiveUser = GetEffectiveUser(user);
            var relativePath = NormalizeRelativePath(path);

            if (!HasPermission(effectiveUser, relativePath, FilePermission.Write))
            {
                return Results.Forbid();
            }

            var targetDirectory = ResolvePath(relativePath);
            if (targetDirectory is null || !Directory.Exists(targetDirectory))
            {
                return Results.BadRequest("Target directory does not exist.");
            }

            if (HasActiveDirectoryDownloadConflict(relativePath))
            {
                return Results.Conflict("The target directory is currently being downloaded. Please try again later.");
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest("Multipart form-data is required.");
            }

            var form = await request.ReadFormAsync(token);
            var files = form.Files;
            if (files.Count == 0)
            {
                return Results.BadRequest("No files were uploaded.");
            }

            await _uploadLimiter.WaitAsync(token);
            try
            {
                using var uploadLock = await AcquirePathLockAsync(relativePath, token);
            foreach (var file in files)
            {
                if (file.Length <= 0)
                {
                    continue;
                }

                var safeFileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(safeFileName))
                {
                    continue;
                }

                var destinationPath = Path.Combine(targetDirectory, safeFileName);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return Results.Conflict($"File already exists: {safeFileName}");
                }

                await using var targetStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var sourceStream = file.OpenReadStream();
                await sourceStream.CopyToAsync(targetStream, token);
            }
            }
            finally
            {
                _uploadLimiter.Release();
            }
            return Results.Ok();
        });

        app.MapPost("/api/upload-file", async (HttpRequest request, string? user, string? path, string? name, CancellationToken token) =>
        {
            TrackClient(request.HttpContext);
            var effectiveUser = GetEffectiveUser(user);
            var relativePath = NormalizeRelativePath(path);

            if (!HasPermission(effectiveUser, relativePath, FilePermission.Write))
            {
                return Results.Forbid();
            }

            var targetDirectory = ResolvePath(relativePath);
            if (targetDirectory is null || !Directory.Exists(targetDirectory))
            {
                return Results.BadRequest("Target directory does not exist.");
            }

            if (HasActiveDirectoryDownloadConflict(relativePath))
            {
                return Results.Conflict("The target directory is currently being downloaded. Please try again later.");
            }

            var safeFileName = Path.GetFileName(name ?? string.Empty);

            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                return Results.BadRequest("File name is required.");
            }

            await _uploadLimiter.WaitAsync(token);
            string? destinationPath = null;
            try
            {
                using var uploadLock = await AcquirePathLockAsync(relativePath, token);
                destinationPath = Path.Combine(targetDirectory, safeFileName);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return Results.Conflict($"File already exists: {safeFileName}");
                }

                await using var targetStream = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    StreamCopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                await request.Body.CopyToAsync(targetStream, token);
                await targetStream.FlushAsync(token);
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(destinationPath) && File.Exists(destinationPath))
                {
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
            finally
            {
                _uploadLimiter.Release();
            }

            return Results.Ok();
        });

        app.MapDelete("/api/entry", (HttpContext context, string? user, string path) =>
        {
            TrackClient(context);
            var effectiveUser = GetEffectiveUser(user);
            var relativePath = NormalizeRelativePath(path);

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return Results.BadRequest("Root directory cannot be deleted.");
            }

            if (!HasPermission(effectiveUser, relativePath, FilePermission.Delete))
            {
                return Results.Forbid();
            }

            if (HasActiveDirectoryDownloadConflict(relativePath))
            {
                return Results.Conflict("The target path is currently involved in an active directory download.");
            }

            var fullPath = ResolvePath(relativePath);
            if (fullPath is null)
            {
                return Results.BadRequest("Invalid path.");
            }

            using var deleteLock = AcquirePathLock(relativePath);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Results.Ok();
            }

            if (Directory.Exists(fullPath))
            {
                EnsureDeleteAuthorizedForDirectory(effectiveUser, relativePath, fullPath);
                Directory.Delete(fullPath, recursive: true);
                return Results.Ok();
            }

            return Results.NotFound();
        });

        app.MapPost("/api/folder", (HttpContext context, string? user, string? path, string? name) =>
        {
            TrackClient(context);
            var effectiveUser = GetEffectiveUser(user);
            var parentRelativePath = NormalizeRelativePath(path);
            var folderName = Path.GetFileName(name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(folderName))
            {
                return Results.BadRequest("Folder name is required.");
            }

            if (!HasPermission(effectiveUser, parentRelativePath, FilePermission.Write))
            {
                return Results.Forbid();
            }

            if (HasActiveDirectoryDownloadConflict(parentRelativePath))
            {
                return Results.Conflict("The target directory is currently being downloaded. Please try again later.");
            }

            var targetDirectory = ResolvePath(parentRelativePath);
            if (targetDirectory is null || !Directory.Exists(targetDirectory))
            {
                return Results.BadRequest("Target directory does not exist.");
            }

            using var folderLock = AcquirePathLock(parentRelativePath);
            var destinationPath = Path.Combine(targetDirectory, folderName);
            if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            {
                return Results.Conflict("A file or folder with the same name already exists.");
            }

            Directory.CreateDirectory(destinationPath);
            return Results.Ok();
        });

        _webApplication = app;
        await _webApplication.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_webApplication is null)
        {
            return;
        }

        await _webApplication.StopAsync(cancellationToken);
        await _webApplication.DisposeAsync();
        _webApplication = null;
        BaseAddress = null;
        _recentClients.Clear();
        _activeDirectoryDownloads.Clear();
        _pathSemaphores.Clear();
    }

    public Task<BrowseResult> BrowseAsync(
        string userName,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(userName, relativePath, FilePermission.Read);
        var result = BrowseInternal(userName, relativePath);
        return Task.FromResult(result);
    }

    public bool HasPermission(string userName, string relativePath, FilePermission permission)
    {
        return _permissionService.HasPermission(_permissionConfig, userName, relativePath, permission);
    }

    public IReadOnlyList<ConnectedClientInfo> GetRecentClientsSnapshot()
    {
        return _recentClients.Values
            .OrderByDescending(item => item.LastSeenLocalTime)
            .ThenBy(item => item.SystemName)
            .ToArray();
    }

    private BrowseResult BrowseInternal(string userName, string relativePath)
    {
        var directoryPath = ResolvePath(relativePath) ?? _sharedFolderPath;
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return new BrowseResult
            {
                Entries = Array.Empty<BrowseEntry>(),
                CanWriteCurrentDirectory = false
            };
        }

        var entries = Directory.EnumerateFileSystemEntries(directoryPath)
            .Select(path =>
            {
                var isDirectory = Directory.Exists(path);
                var info = isDirectory
                    ? new DirectoryInfo(path) as FileSystemInfo
                    : new FileInfo(path);
                var childRelativePath = NormalizeRelativePath(Path.GetRelativePath(_sharedFolderPath!, path));

                return new BrowseEntry
                {
                    Name = Path.GetFileName(path),
                    RelativePath = childRelativePath,
                    IsDirectory = isDirectory,
                    Size = info is FileInfo fileInfo ? fileInfo.Length : 0L,
                    LastModified = info.LastWriteTimeUtc,
                    CanRead = HasPermission(userName, childRelativePath, FilePermission.Read),
                    CanWrite = isDirectory && HasPermission(userName, childRelativePath, FilePermission.Write),
                    CanDelete = HasPermission(userName, childRelativePath, FilePermission.Delete)
                };
            })
            .Where(entry => entry.CanRead)
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name)
            .ToArray();

        return new BrowseResult
        {
            Entries = entries,
            CanWriteCurrentDirectory = HasPermission(userName, NormalizeRelativePath(relativePath), FilePermission.Write)
        };
    }

    private void EnsureDeleteAuthorizedForDirectory(string userName, string relativePath, string fullPath)
    {
        var descendants = Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(_sharedFolderPath!, path)));

        foreach (var descendant in descendants)
        {
            EnsureAuthorized(userName, descendant, FilePermission.Delete);
        }

        EnsureAuthorized(userName, relativePath, FilePermission.Delete);
    }

    private async Task WriteDirectoryToArchiveAsync(
        ZipArchive archive,
        string userName,
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directoryPath in Directory.EnumerateDirectories(fullPath, "*", enumerationOptions))
        {
            var childRelativePath = NormalizeRelativePath(Path.GetRelativePath(_sharedFolderPath!, directoryPath));
            if (!HasPermission(userName, childRelativePath, FilePermission.Read))
            {
                continue;
            }

            try
            {
                var archiveEntryName = Path.GetRelativePath(fullPath, directoryPath).Replace('\\', '/') + "/";
                archive.CreateEntry(archiveEntryName);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(fullPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var childRelativePath = NormalizeRelativePath(Path.GetRelativePath(_sharedFolderPath!, filePath));
            if (!HasPermission(userName, childRelativePath, FilePermission.Read))
            {
                continue;
            }

            try
            {
                var archiveEntryName = Path.GetRelativePath(fullPath, filePath).Replace('\\', '/');
                var entry = archive.CreateEntry(archiveEntryName, CompressionLevel.NoCompression);
                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                throw new DirectoryDownloadException(filePath, "访问被拒绝或文件正被其它程序独占。");
            }
            catch (IOException ex)
            {
                throw new DirectoryDownloadException(filePath, ex.Message, ex);
            }
            catch (Exception ex) when (ex is not DirectoryDownloadException)
            {
                throw new DirectoryDownloadException(filePath, ex.Message, ex);
            }
        }
    }

    private async Task<IDisposable> AcquireDirectoryDownloadLeaseAsync(string relativePath, CancellationToken cancellationToken)
    {
        await _directoryDownloadLimiter.WaitAsync(cancellationToken);
        try
        {
            return RegisterActiveDirectoryDownload(relativePath);
        }
        catch
        {
            _directoryDownloadLimiter.Release();
            throw;
        }
    }

    private async Task<IDisposable> AcquirePathLockAsync(string relativePath, CancellationToken cancellationToken)
    {
        var key = BuildLockKey(relativePath);
        var semaphore = _pathSemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private IDisposable AcquirePathLock(string relativePath)
    {
        var key = BuildLockKey(relativePath);
        var semaphore = _pathSemaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        semaphore.Wait();
        return new SemaphoreReleaser(semaphore);
    }

    private IDisposable RegisterActiveDirectoryDownload(string relativePath)
    {
        var key = NormalizeRelativePath(relativePath);
        _activeDirectoryDownloads.AddOrUpdate(key, 1, (_, count) => count + 1);
        return new ActiveDirectoryDownloadReleaser(this, key);
    }

    private void ReleaseActiveDirectoryDownload(string relativePath)
    {
        _activeDirectoryDownloads.AddOrUpdate(
            relativePath,
            0,
            (_, count) => count > 0 ? count - 1 : 0);

        if (_activeDirectoryDownloads.TryGetValue(relativePath, out var count) && count <= 0)
        {
            _activeDirectoryDownloads.TryRemove(relativePath, out _);
        }
    }

    private bool HasActiveDirectoryDownloadConflict(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        return _activeDirectoryDownloads.Keys.Any(activePath => PathsOverlap(normalizedPath, activePath));
    }

    private static string BuildLockKey(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        return string.IsNullOrWhiteSpace(normalizedPath) ? "/" : normalizedPath;
    }

    private static bool PathsOverlap(string left, string right)
    {
        var normalizedLeft = NormalizeRelativePath(left);
        var normalizedRight = NormalizeRelativePath(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return true;
        }

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedLeft.StartsWith(normalizedRight + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedRight.StartsWith(normalizedLeft + "/", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDirectoryDownloadFailure(string requestedRelativePath, string failedPath, Exception exception)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(_diagnosticLogPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var message = string.Join(
                Environment.NewLine,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 目录下载失败",
                $"请求路径: {requestedRelativePath}",
                $"失败文件: {failedPath}",
                $"异常类型: {exception.GetType().FullName}",
                $"异常消息: {exception.Message}",
                exception.StackTrace ?? string.Empty,
                new string('-', 80));

            File.AppendAllText(_diagnosticLogPath, message + Environment.NewLine, System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void LogDirectoryDownloadTrace(string stage, string detail)
    {
        try
        {
            var logDirectory = Path.GetDirectoryName(_diagnosticLogPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var message = string.Join(
                Environment.NewLine,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {stage}",
                detail,
                new string('-', 80));

            File.AppendAllText(_diagnosticLogPath, message + Environment.NewLine, System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    private sealed class DirectoryDownloadException : IOException
    {
        public DirectoryDownloadException(string failedPath, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            FailedPath = failedPath;
        }

        public string FailedPath { get; }
    }

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Release();
        }
    }

    private sealed class ActiveDirectoryDownloadReleaser : IDisposable
    {
        private readonly FileShareServerService _owner;
        private readonly string _relativePath;
        private bool _disposed;

        public ActiveDirectoryDownloadReleaser(FileShareServerService owner, string relativePath)
        {
            _owner = owner;
            _relativePath = relativePath;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.ReleaseActiveDirectoryDownload(_relativePath);
            _owner._directoryDownloadLimiter.Release();
        }
    }

    private string? ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_sharedFolderPath))
        {
            return null;
        }

        var combinedPath = Path.GetFullPath(Path.Combine(_sharedFolderPath, relativePath));
        var normalizedRoot = Path.GetFullPath(_sharedFolderPath);

        return combinedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? combinedPath
            : null;
    }

    private void EnsureAuthorized(string userName, string relativePath, FilePermission permission)
    {
        if (!HasPermission(userName, relativePath, permission))
        {
            throw new UnauthorizedAccessException($"User '{userName}' does not have '{permission}' permission for '{relativePath}'.");
        }
    }

    private static string GetEffectiveUser(string? userName)
    {
        return string.IsNullOrWhiteSpace(userName) ? "guest" : userName.Trim();
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').Trim().Trim('/');
    }

    private void TrackClient(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            return;
        }

        var systemName = context.Request.Headers[ClientNameHeader].FirstOrDefault()?.Trim() ?? string.Empty;
        var key = $"{remoteIp}|{systemName}";

        _recentClients.AddOrUpdate(
            key,
            _ => new ConnectedClientInfo
            {
                IpAddress = remoteIp,
                SystemName = systemName,
                LastSeenLocalTime = DateTime.Now
            },
            (_, existing) =>
            {
                existing.LastSeenLocalTime = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(systemName))
                {
                    existing.SystemName = systemName;
                }

                return existing;
            });
    }
}
