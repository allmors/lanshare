using System.Collections.Concurrent;
using System.IO;
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
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();
    private readonly ConcurrentDictionary<string, ConnectedClientInfo> _recentClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPermissionService _permissionService;
    private WebApplication? _webApplication;
    private string? _sharedFolderPath;
    private PermissionConfig _permissionConfig = new();

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

        _sharedFolderPath = options.SharedFolderPath;
        _permissionConfig = options.Permissions ?? new PermissionConfig();
        BaseAddress = $"http://0.0.0.0:{options.ServicePort}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BaseAddress);
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.Limits.MaxRequestBodySize = null;
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

                await using var targetStream = File.Create(destinationPath);
                await using var sourceStream = file.OpenReadStream();
                await sourceStream.CopyToAsync(targetStream, token);
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

            var fullPath = ResolvePath(relativePath);
            if (fullPath is null)
            {
                return Results.BadRequest("Invalid path.");
            }

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

            var targetDirectory = ResolvePath(parentRelativePath);
            if (targetDirectory is null || !Directory.Exists(targetDirectory))
            {
                return Results.BadRequest("Target directory does not exist.");
            }

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
