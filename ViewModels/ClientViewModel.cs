using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using LanShare.Infrastructure;
using LanShare.Models;
using LanShare.Services.Client;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace LanShare.ViewModels;

public sealed class ClientViewModel : BindableBase
{
    private readonly AppConfig _config;
    private readonly IConfigurationService _configurationService;
    private readonly IServiceDiscoveryClient _discoveryClient;
    private readonly IFileShareClientService _fileShareClientService;
    private readonly Action<string> _statusCallback;
    private CancellationTokenSource? _transferCancellationTokenSource;

    private DiscoveredServer? _selectedServer;
    private BrowseEntry? _selectedEntry;
    private string _clientStatus = "等待连接共享服务";
    private string _serverAddressInput = string.Empty;
    private string _currentRelativePath = string.Empty;
    private bool _currentDirectoryCanWrite;
    private bool _isTransferActive;
    private bool _isTransferIndeterminate;
    private double _transferProgressValue;
    private string _transferProgressText = "等待传输";
    private string _transferTitle = "当前没有进行中的传输";
    private bool _isServerAddressInputEnabled;
    private bool _isBuiltInServerConnected;
    private bool _initialized;

    public ClientViewModel(
        AppConfig config,
        IConfigurationService configurationService,
        IServiceDiscoveryClient discoveryClient,
        IFileShareClientService fileShareClientService,
        Action<string> statusCallback)
    {
        _config = config;
        _configurationService = configurationService;
        _discoveryClient = discoveryClient;
        _fileShareClientService = fileShareClientService;
        _statusCallback = statusCallback;

        EnsureClientConfigDefaults();
        _serverAddressInput = BuildBuiltInServerAddress();

        ConnectToServerCommand = new AsyncRelayCommand(
            ConnectToServerAsync,
            () => !string.IsNullOrWhiteSpace(ServerAddressInput) && !IsTransferActive,
            HandleError);
        DiscoverServersCommand = new AsyncRelayCommand(
            // Keep LAN discovery code for future use, but do not use it in the current client flow.
            () => DiscoverServersAsync(autoConnect: false, unlockManualInputOnFailure: true),
            () => !IsTransferActive && !IsBuiltInServerConnected,
            HandleError);
        BrowseSelectedServerCommand = new AsyncRelayCommand(
            BrowseSelectedServerAsync,
            () => !IsTransferActive,
            HandleError);
        DownloadSelectedItemCommand = new AsyncRelayCommand(
            DownloadSelectedItemAsync,
            () => SelectedServer is not null && SelectedEntry is not null && !IsTransferActive,
            HandleError);
        OpenSelectedDirectoryCommand = new AsyncRelayCommand(
            OpenSelectedDirectoryAsync,
            () => SelectedEntry is { IsDirectory: true } && SelectedServer is not null && !IsTransferActive,
            HandleError);
        NavigateUpCommand = new AsyncRelayCommand(
            NavigateUpAsync,
            () => SelectedServer is not null && !string.IsNullOrWhiteSpace(CurrentRelativePath) && !IsTransferActive,
            HandleError);
        UploadToSelectedDirectoryCommand = new AsyncRelayCommand(
            UploadToCurrentDirectoryAsync,
            () => SelectedServer is not null && CurrentDirectoryCanWrite && !IsTransferActive,
            HandleError);
        DeleteSelectedEntryCommand = new AsyncRelayCommand(
            DeleteSelectedEntryAsync,
            () => SelectedServer is not null && SelectedEntry is { CanDelete: true } && !IsTransferActive,
            HandleError);
        CreateFolderCommand = new AsyncRelayCommand(
            CreateFolderAsync,
            () => SelectedServer is not null && CurrentDirectoryCanWrite && !IsTransferActive,
            HandleError);
        BrowseDownloadFolderCommand = new RelayCommand(BrowseDownloadFolder);
        CancelTransferCommand = new RelayCommand(CancelTransfer, () => IsTransferActive);
    }

    public ObservableCollection<DiscoveredServer> Servers { get; } = new();

    public ObservableCollection<BrowseEntry> Entries { get; } = new();

    public string DownloadFolder
    {
        get => _config.Client.DownloadFolder;
        set
        {
            if (_config.Client.DownloadFolder != value)
            {
                _config.Client.DownloadFolder = value;
                RaisePropertyChanged();
            }
        }
    }

    public string ServerAddressInput
    {
        get => _serverAddressInput;
        set
        {
            if (SetProperty(ref _serverAddressInput, value))
            {
                ConnectToServerCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsServerAddressInputEnabled
    {
        get => _isServerAddressInputEnabled;
        private set => SetProperty(ref _isServerAddressInputEnabled, value);
    }

    public bool IsBuiltInServerConnected
    {
        get => _isBuiltInServerConnected;
        private set
        {
            if (SetProperty(ref _isBuiltInServerConnected, value))
            {
                DiscoverServersCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DiscoveredServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                MarkActiveServer(value);
                ResetCurrentBrowseState();
                UpdateBuiltInServerConnectionState(value);
                ClientStatus = value is null ? "等待连接共享服务" : "已连接共享服务";
                _statusCallback(ClientStatus);
                RaisePropertyChanged(nameof(RefreshButtonText));
                RaiseCommandStates();
            }
        }
    }

    public BrowseEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ClientStatus
    {
        get => _clientStatus;
        private set => SetProperty(ref _clientStatus, value);
    }

    public string CurrentRelativePath
    {
        get => _currentRelativePath;
        private set
        {
            if (SetProperty(ref _currentRelativePath, value))
            {
                RaisePropertyChanged(nameof(CurrentPathDisplay));
                RaiseCommandStates();
            }
        }
    }

    public string CurrentPathDisplay => string.IsNullOrWhiteSpace(CurrentRelativePath) ? "/" : $"/{CurrentRelativePath}";

    public bool CurrentDirectoryCanWrite
    {
        get => _currentDirectoryCanWrite;
        private set
        {
            if (SetProperty(ref _currentDirectoryCanWrite, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsTransferActive
    {
        get => _isTransferActive;
        private set
        {
            if (SetProperty(ref _isTransferActive, value))
            {
                RaiseCommandStates();
                ConnectToServerCommand.RaiseCanExecuteChanged();
                DiscoverServersCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsTransferIndeterminate
    {
        get => _isTransferIndeterminate;
        private set => SetProperty(ref _isTransferIndeterminate, value);
    }

    public double TransferProgressValue
    {
        get => _transferProgressValue;
        private set => SetProperty(ref _transferProgressValue, value);
    }

    public string TransferProgressText
    {
        get => _transferProgressText;
        private set => SetProperty(ref _transferProgressText, value);
    }

    public string TransferTitle
    {
        get => _transferTitle;
        private set => SetProperty(ref _transferTitle, value);
    }

    public string RefreshButtonText => SelectedServer is null ? "重试" : "刷新";

    public AsyncRelayCommand ConnectToServerCommand { get; }

    public AsyncRelayCommand DiscoverServersCommand { get; }

    public AsyncRelayCommand BrowseSelectedServerCommand { get; }

    public AsyncRelayCommand DownloadSelectedItemCommand { get; }

    public AsyncRelayCommand OpenSelectedDirectoryCommand { get; }

    public AsyncRelayCommand NavigateUpCommand { get; }

    public AsyncRelayCommand UploadToSelectedDirectoryCommand { get; }

    public AsyncRelayCommand DeleteSelectedEntryCommand { get; }

    public AsyncRelayCommand CreateFolderCommand { get; }

    public RelayCommand BrowseDownloadFolderCommand { get; }

    public RelayCommand CancelTransferCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        EnsureClientConfigDefaults();
        IsServerAddressInputEnabled = false;
        IsBuiltInServerConnected = false;
        ServerAddressInput = BuildBuiltInServerAddress();

        if (!_config.Client.AutoConnectPreferredServerOnStartup)
        {
            return;
        }

        await ConnectBuiltInServerWithRetryAsync();
    }

    public bool CanAcceptFileDrop()
    {
        return SelectedServer is not null && CurrentDirectoryCanWrite && !IsTransferActive;
    }

    public async Task UploadFilesFromDropAsync(IEnumerable<string> filePaths)
    {
        var droppedItems = filePaths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (droppedItems.Length == 0)
        {
            return;
        }

        if (!CanAcceptFileDrop())
        {
            throw new InvalidOperationException("当前目录没有上传权限。");
        }

        var targetLabel = string.IsNullOrWhiteSpace(CurrentRelativePath) ? "共享根目录" : CurrentRelativePath;
        var folderCount = droppedItems.Count(Directory.Exists);
        var fileCount = droppedItems.Count(File.Exists);
        var uploadSummary = BuildUploadSelectionSummary(fileCount, folderCount);
        var confirm = MessageBox.Show(
            $"准备上传 {uploadSummary} 到“{targetLabel}”，是否继续？",
            "确认上传",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await UploadItemsToCurrentDirectoryAsync(droppedItems);
    }

    #pragma warning disable CS0162
    private async Task ConnectBuiltInServerWithRetryAsync()
    {
        var builtInAddress = BuildBuiltInServerAddress();
        ServerAddressInput = builtInAddress;
        IsServerAddressInputEnabled = false;
        IsBuiltInServerConnected = false;

        var connectTimeoutSeconds = Math.Max(2, _config.Discovery.DiscoveryTimeoutSeconds);
        const int maxRetryCount = 3;

        for (var attempt = 1; attempt <= maxRetryCount; attempt++)
        {
            ClientStatus = $"正在连接内置共享服务，第 {attempt}/{maxRetryCount} 次...";
            _statusCallback(ClientStatus);

            if (await TryConnectToAddressAsync(builtInAddress, connectTimeoutSeconds))
            {
                return;
            }
        }

        ClientStatus = "内置共享服务连接失败，请点击重试。";
        _statusCallback(ClientStatus);
        return;
        ClientStatus = "正在连接共享服务...";
        _statusCallback(ClientStatus);

        if (await TryConnectToAddressAsync(builtInAddress, connectTimeoutSeconds))
        {
            return;
        }

        ClientStatus = $"内置服务地址不可用，正在自动发现局域网服务，最长 {_config.Discovery.DiscoveryTimeoutSeconds} 秒…";
        _statusCallback(ClientStatus);
        await DiscoverServersAsync(autoConnect: true, unlockManualInputOnFailure: true);
    }

    private async Task ConnectToServerAsync()
    {
        var address = string.IsNullOrWhiteSpace(ServerAddressInput)
            ? BuildBuiltInServerAddress()
            : ServerAddressInput.Trim();

        if (string.Equals(address.TrimEnd('/'), BuildBuiltInServerAddress().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            await ConnectBuiltInServerWithRetryAsync();
            return;
        }

        await ConnectToAddressAsync(address, Math.Max(3, _config.Discovery.DiscoveryTimeoutSeconds + 1));
    }
    #pragma warning restore CS0162

    private async Task DiscoverServersAsync(bool autoConnect, bool unlockManualInputOnFailure)
    {
        if (IsBuiltInServerConnected)
        {
            ClientStatus = "当前已连接共享服务，无需再次发现。";
            _statusCallback(ClientStatus);
            return;
        }

        ClientStatus = $"正在发现局域网服务，最长 {_config.Discovery.DiscoveryTimeoutSeconds} 秒…";
        _statusCallback(ClientStatus);

        var results = await _discoveryClient.DiscoverAsync(_config.Discovery);
        Servers.Clear();

        foreach (var server in results)
        {
            Servers.Add(server);
        }

        if (Servers.Count == 0)
        {
            IsServerAddressInputEnabled = unlockManualInputOnFailure;
            ServerAddressInput = BuildBuiltInServerAddress();
            IsBuiltInServerConnected = false;
            ClientStatus = "未发现可用服务，已允许手动输入共享地址。";
            _statusCallback(ClientStatus);
            return;
        }

        IsServerAddressInputEnabled = false;

        if (!autoConnect)
        {
            SelectedServer = Servers[0];
            ServerAddressInput = $"{SelectedServer.BaseAddress}/";
            ClientStatus = $"已发现 {Servers.Count} 台服务端，请点击刷新加载共享目录。";
            _statusCallback(ClientStatus);
            return;
        }

        foreach (var server in Servers)
        {
            if (await TryConnectToDiscoveredServerAsync(server, Math.Max(2, _config.Discovery.DiscoveryTimeoutSeconds)))
            {
                ClientStatus = "已自动连接到共享服务";
                _statusCallback(ClientStatus);
                return;
            }
        }

        SelectedServer = Servers[0];
        ServerAddressInput = $"{SelectedServer.BaseAddress}/";
        ClientStatus = "已发现服务端，但自动打开共享目录失败，请点击连接或刷新重试。";
        _statusCallback(ClientStatus);
    }

    private async Task BrowseSelectedServerAsync()
    {
        if (SelectedServer is null)
        {
            await ConnectBuiltInServerWithRetryAsync();
            return;
        }

        await LoadEntriesAsync(CurrentRelativePath);
    }

    private async Task OpenSelectedDirectoryAsync()
    {
        if (SelectedEntry is not { IsDirectory: true })
        {
            return;
        }

        CurrentRelativePath = NormalizeRelativePath(SelectedEntry.RelativePath);
        await LoadEntriesAsync(CurrentRelativePath);
    }

    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentRelativePath))
        {
            return;
        }

        var normalized = NormalizeRelativePath(CurrentRelativePath);
        var lastSeparator = normalized.LastIndexOf('/');
        CurrentRelativePath = lastSeparator >= 0 ? normalized[..lastSeparator] : string.Empty;
        UpdateAddressBarFromCurrentState();
        await LoadEntriesAsync(CurrentRelativePath);
    }

    private async Task UploadToCurrentDirectoryAsync()
    {
        if (!CanAcceptFileDrop())
        {
            return;
        }

        var currentDirectoryName = string.IsNullOrWhiteSpace(CurrentRelativePath)
            ? "共享根目录"
            : CurrentRelativePath.Split('/').Last();

        var uploadMode = MessageBox.Show(
            $"上传到 {currentDirectoryName}\n\n选择“是”上传文件，选择“否”上传整个文件夹。",
            "选择上传内容",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (uploadMode == MessageBoxResult.Cancel)
        {
            return;
        }

        if (uploadMode == MessageBoxResult.No)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = $"选择要上传到 {currentDirectoryName} 的文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
            {
                return;
            }

            await UploadItemsToCurrentDirectoryAsync([folderDialog.SelectedPath]);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = $"选择要上传到 {currentDirectoryName} 的文件"
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        await UploadItemsToCurrentDirectoryAsync(dialog.FileNames);
    }

    private async Task UploadItemsToCurrentDirectoryAsync(IReadOnlyList<string> inputPaths)
    {
        if (SelectedServer is null)
        {
            return;
        }

        var uploadPlan = BuildUploadPlan(CurrentRelativePath, inputPaths);
        if (uploadPlan.FileCount == 0)
        {
            throw new InvalidOperationException("没有找到可上传的文件。");
        }

        var cancellationToken = BeginTransfer("准备上传文件...", false);

        var duplicateFiles = await CollectDuplicateFilesAsync(SelectedServer, uploadPlan, cancellationToken);
        if (duplicateFiles.Count == uploadPlan.FileCount)
        {
            throw new InvalidOperationException("检测到上传文件全部已存在，已取消上传。");
        }

        if (duplicateFiles.Count > 0)
        {
            var preview = string.Join(Environment.NewLine, duplicateFiles.Take(8));
            var suffix = duplicateFiles.Count > 8 ? $"{Environment.NewLine}..." : string.Empty;
            var result = MessageBox.Show(
                $"检测到 {duplicateFiles.Count} 个同名文件已存在，将跳过这些文件继续上传：{Environment.NewLine}{preview}{suffix}",
                "发现重复文件",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        var targetLabel = string.IsNullOrWhiteSpace(CurrentRelativePath) ? "共享根目录" : CurrentRelativePath;

        var progress = new Progress<TransferProgressInfo>(UpdateTransferProgress);

        foreach (var directoryPath in uploadPlan.DirectoriesToEnsure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureRemoteDirectoryExistsAsync(SelectedServer, directoryPath, cancellationToken);
        }

        var uploadedFileCount = 0;
        foreach (var batch in uploadPlan.Batches)
        {
            var filesToUpload = batch.FilePaths
                .Where(filePath => !duplicateFiles.Contains(BuildRemoteFilePath(batch.TargetRelativePath, Path.GetFileName(filePath))))
                .ToArray();

            if (filesToUpload.Length == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _fileShareClientService.UploadFilesAsync(
                SelectedServer,
                batch.TargetRelativePath,
                filesToUpload,
                progress,
                cancellationToken);
            uploadedFileCount += filesToUpload.Length;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await LoadEntriesAsync(CurrentRelativePath);
        CompleteTransfer(
            duplicateFiles.Count > 0
                ? $"已上传 {uploadedFileCount} 个文件到 {targetLabel}，跳过 {duplicateFiles.Count} 个重复文件"
                : $"已上传 {uploadedFileCount} 个文件到 {targetLabel}");
    }

    private async Task DeleteSelectedEntryAsync()
    {
        if (SelectedServer is null || SelectedEntry is null || !SelectedEntry.CanDelete)
        {
            return;
        }

        var targetLabel = SelectedEntry.IsDirectory ? "目录" : "文件";
        var result = MessageBox.Show(
            $"确认删除{targetLabel}“{SelectedEntry.Name}”吗？此操作不可撤销。",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var deletedName = SelectedEntry.Name;
        await _fileShareClientService.DeleteEntryAsync(SelectedServer, SelectedEntry);
        await LoadEntriesAsync(CurrentRelativePath);

        ClientStatus = $"已删除 {deletedName}";
        _statusCallback(ClientStatus);
    }

    private async Task CreateFolderAsync()
    {
        if (SelectedServer is null || !CurrentDirectoryCanWrite)
        {
            return;
        }

        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = new TextPromptDialog("新建文件夹", "请输入文件夹名称")
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folderName = dialog.InputText;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        await _fileShareClientService.CreateFolderAsync(SelectedServer, CurrentRelativePath, folderName);
        await LoadEntriesAsync(CurrentRelativePath);

        ClientStatus = $"已创建文件夹 {folderName}";
        _statusCallback(ClientStatus);
    }

    private async Task LoadEntriesAsync(string relativePath)
    {
        if (SelectedServer is null)
        {
            return;
        }

        Entries.Clear();
        SelectedEntry = null;
        CurrentRelativePath = NormalizeRelativePath(relativePath);
        UpdateAddressBarFromCurrentState();

        var result = await _fileShareClientService.BrowseAsync(SelectedServer, CurrentRelativePath);
        ApplyBrowseResult(result, CurrentRelativePath);

        ClientStatus = "已连接共享服务";
        _statusCallback(ClientStatus);
    }

    private async Task DownloadSelectedItemAsync()
    {
        if (SelectedServer is null || SelectedEntry is null)
        {
            return;
        }

        var targetFolder = string.IsNullOrWhiteSpace(DownloadFolder)
            ? GetDefaultDownloadFolder()
            : DownloadFolder;

        DownloadFolder = targetFolder;

        var cancellationToken = BeginTransfer(SelectedEntry.IsDirectory ? "准备下载目录..." : "准备下载文件...", false);
        var progress = new Progress<TransferProgressInfo>(UpdateTransferProgress);
        await _fileShareClientService.DownloadEntryAsync(SelectedServer, SelectedEntry, targetFolder, progress, cancellationToken);
        CompleteTransfer(SelectedEntry.IsDirectory
            ? $"目录已下载到 {targetFolder}"
            : $"文件已下载到 {targetFolder}");
    }

    private void BrowseDownloadFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择默认下载目录",
            ShowNewFolderButton = true,
            SelectedPath = string.IsNullOrWhiteSpace(DownloadFolder)
                ? GetDefaultDownloadFolder()
                : DownloadFolder
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        DownloadFolder = dialog.SelectedPath;
        _configurationService.Save(_config);
        _statusCallback($"下载目录已更新为：{dialog.SelectedPath}");
    }

    private void HandleError(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            EndTransferAsCanceled();
            ClientStatus = "已取消当前传输";
            _statusCallback(ClientStatus);
            return;
        }

        EndTransferAfterFailure();
        ClientStatus = $"操作失败：{BuildUserFriendlyErrorMessage(ex)}";
        _statusCallback(ClientStatus);
    }

    private static string BuildUserFriendlyErrorMessage(Exception ex)
    {
        var message = ex.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return "请稍后重试。";
        }

        if (ex is HttpRequestException || ex.InnerException is System.Net.Sockets.SocketException)
        {
            if (message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("由于目标计算机积极拒绝", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("无法连接", StringComparison.OrdinalIgnoreCase))
            {
                return "共享服务当前不可用，请确认服务端已启动后重试。";
            }
        }

        return message;
    }

    private void RaiseCommandStates()
    {
        BrowseSelectedServerCommand.RaiseCanExecuteChanged();
        DownloadSelectedItemCommand.RaiseCanExecuteChanged();
        OpenSelectedDirectoryCommand.RaiseCanExecuteChanged();
        NavigateUpCommand.RaiseCanExecuteChanged();
        UploadToSelectedDirectoryCommand.RaiseCanExecuteChanged();
        DeleteSelectedEntryCommand.RaiseCanExecuteChanged();
        CreateFolderCommand.RaiseCanExecuteChanged();
        DiscoverServersCommand.RaiseCanExecuteChanged();
        CancelTransferCommand.RaiseCanExecuteChanged();
    }

    private CancellationToken BeginTransfer(string title, bool indeterminate)
    {
        DisposeTransferCancellationTokenSource();
        _transferCancellationTokenSource = new CancellationTokenSource();
        IsTransferActive = true;
        TransferTitle = title;
        IsTransferIndeterminate = indeterminate;
        TransferProgressValue = 0;
        TransferProgressText = indeterminate ? "传输中..." : "0%";
        return _transferCancellationTokenSource.Token;
    }

    private void CompleteTransfer(string statusMessage)
    {
        DisposeTransferCancellationTokenSource();
        IsTransferActive = false;
        IsTransferIndeterminate = false;
        TransferProgressValue = 100;
        TransferProgressText = "100.0%";
        TransferTitle = "传输完成";
        ClientStatus = statusMessage;
        _statusCallback(statusMessage);
    }

    private void EndTransferAfterFailure()
    {
        DisposeTransferCancellationTokenSource();
        IsTransferActive = false;
        IsTransferIndeterminate = false;
        TransferTitle = "传输已中断";
    }

    private void EndTransferAsCanceled()
    {
        DisposeTransferCancellationTokenSource();
        IsTransferActive = false;
        IsTransferIndeterminate = false;
        TransferTitle = "传输已取消";
        TransferProgressText = "已取消";
    }

    private void UpdateTransferProgress(TransferProgressInfo progress)
    {
        TransferTitle = progress.FileCount > 1
            ? $"{progress.Operation} {progress.FileIndex}/{progress.FileCount}: {progress.FileName}"
            : $"{progress.Operation}: {progress.FileName}";

        if (progress.FileCount > 1 && progress.OverallTotalBytes.HasValue && progress.OverallTotalBytes.Value > 0)
        {
            IsTransferIndeterminate = false;

            var fileBasedProgress = progress.IsCompleted
                ? progress.FileIndex
                : Math.Max(progress.FileIndex - 1, 0);
            var progressValue = progress.FileCount <= 0
                ? 0
                : Math.Round(fileBasedProgress * 100d / progress.FileCount, 1);

            TransferProgressValue = progress.IsCompleted && progress.FileIndex >= progress.FileCount
                ? 100
                : Math.Min(progressValue, 99.9);

            var currentTotalBytes = progress.TotalBytes ?? 0;
            var currentBytesTransferred = progress.BytesTransferred;
            var overallBytesTransferred = progress.OverallBytesTransferred ?? 0;
            var overallTotalBytes = progress.OverallTotalBytes.Value;

            TransferProgressText =
                $"{TransferProgressValue:0.0}%   当前文件 {FormatBytes(currentBytesTransferred)} / {FormatBytes(currentTotalBytes)}   总进度 {progress.FileIndex}/{progress.FileCount}   总容量 {FormatBytes(overallBytesTransferred)} / {FormatBytes(overallTotalBytes)}";
        }
        else if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
        {
            IsTransferIndeterminate = false;
            var rawProgressValue = Math.Round(progress.BytesTransferred * 100d / progress.TotalBytes.Value, 1);
            var isFinishing = !progress.IsCompleted && progress.BytesTransferred >= progress.TotalBytes.Value;

            TransferProgressValue = progress.IsCompleted
                ? 100
                : isFinishing
                    ? 99.9
                    : Math.Min(rawProgressValue, 100);

            TransferProgressText = progress.IsCompleted
                ? $"100.0%   {FormatBytes(progress.TotalBytes.Value)} / {FormatBytes(progress.TotalBytes.Value)}"
                : isFinishing
                    ? $"99.9%   {FormatBytes(progress.BytesTransferred)} / {FormatBytes(progress.TotalBytes.Value)}   正在完成..."
                    : $"{TransferProgressValue:0.0}%   {FormatBytes(progress.BytesTransferred)} / {FormatBytes(progress.TotalBytes.Value)}";
        }
        else
        {
            IsTransferIndeterminate = true;
            TransferProgressText = FormatBytes(progress.BytesTransferred);
        }
    }

    private void ResetCurrentBrowseState()
    {
        CurrentRelativePath = string.Empty;
        CurrentDirectoryCanWrite = false;
        Entries.Clear();
        SelectedEntry = null;
    }

    private void MarkActiveServer(DiscoveredServer? activeServer)
    {
        foreach (var server in Servers)
        {
            server.IsActive = ReferenceEquals(server, activeServer);
        }
    }

    private async Task<bool> TryConnectToAddressAsync(string address, int timeoutSeconds)
    {
        try
        {
            await ConnectToAddressAsync(address, timeoutSeconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryConnectToDiscoveredServerAsync(DiscoveredServer server, int timeoutSeconds)
    {
        try
        {
            var initialPath = string.Empty;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var result = await _fileShareClientService.BrowseAsync(server, initialPath, cts.Token);

            ReplaceServers(server);
            SelectedServer = server;
            CurrentRelativePath = initialPath;
            ApplyBrowseResult(result, initialPath);
            UpdateAddressBarFromCurrentState();
            IsServerAddressInputEnabled = false;
            UpdateBuiltInServerConnectionState(server);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ConnectToAddressAsync(string address, int timeoutSeconds)
    {
        var target = ParseManualServerTarget(address);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var result = await _fileShareClientService.BrowseAsync(target.Server, target.InitialRelativePath, cts.Token);

        ReplaceServers(target.Server);
        SelectedServer = target.Server;
        CurrentRelativePath = target.InitialRelativePath;
        ApplyBrowseResult(result, target.InitialRelativePath);
        ServerAddressInput = target.DisplayAddress;
        IsServerAddressInputEnabled = false;
        UpdateBuiltInServerConnectionState(target.Server);

        var status = "已连接到共享服务";

        ClientStatus = status;
        _statusCallback(status);

        _config.Client.PreferredServerAddress = target.DisplayAddress;
        _configurationService.Save(_config);
    }

    private void ApplyBrowseResult(BrowseResult result, string relativePath)
    {
        Entries.Clear();
        SelectedEntry = null;
        CurrentRelativePath = NormalizeRelativePath(relativePath);
        CurrentDirectoryCanWrite = result.CanWriteCurrentDirectory;

        foreach (var entry in result.Entries)
        {
            Entries.Add(entry);
        }

        UpdateAddressBarFromCurrentState();
    }

    private void ReplaceServers(DiscoveredServer server)
    {
        Servers.Clear();
        Servers.Add(server);
    }

    private void UpdateBuiltInServerConnectionState(DiscoveredServer? server)
    {
        if (server is null)
        {
            IsBuiltInServerConnected = false;
            return;
        }

        var builtInUri = NormalizeUri(BuildBuiltInServerAddress());
        var currentUri = NormalizeUri($"{server.BaseAddress}/");

        IsBuiltInServerConnected = builtInUri is not null &&
                                   currentUri is not null &&
                                   string.Equals(builtInUri.Scheme, currentUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(builtInUri.Host, currentUri.Host, StringComparison.OrdinalIgnoreCase) &&
                                   builtInUri.Port == currentUri.Port;
    }

    private static Uri? NormalizeUri(string address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null;
    }

    private ManualServerTarget ParseManualServerTarget(string input)
    {
        var normalized = input.Trim();
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"http://{normalized}";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("共享地址格式不正确，请输入类似 http://192.168.202.163:49443/Atest 的地址。");
        }

        var port = uri.IsDefaultPort ? _config.Server.ServicePort : uri.Port;
        var baseAddress = $"{uri.Scheme}://{uri.Host}:{port}";
        var initialRelativePath = string.Join(
            '/',
            uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString));

        var displayAddress = string.IsNullOrWhiteSpace(initialRelativePath)
            ? $"{baseAddress}/"
            : $"{baseAddress}/{initialRelativePath}";

        var displayName = string.IsNullOrWhiteSpace(initialRelativePath)
            ? $"{uri.Host}:{port}"
            : $"{uri.Host}:{port}/{initialRelativePath}";

        return new ManualServerTarget(
            new DiscoveredServer
            {
                ServerName = displayName,
                IpAddress = uri.Host,
                Version = "手动连接",
                ServicePort = port,
                BaseAddressOverride = baseAddress
            },
            initialRelativePath,
            displayAddress);
    }

    private void UpdateAddressBarFromCurrentState()
    {
        if (SelectedServer is null)
        {
            return;
        }

        ServerAddressInput = string.IsNullOrWhiteSpace(CurrentRelativePath)
            ? $"{SelectedServer.BaseAddress}/"
            : $"{SelectedServer.BaseAddress}/{CurrentRelativePath}";
    }

    private UploadPlan BuildUploadPlan(string currentRelativePath, IReadOnlyList<string> inputPaths)
    {
        var batches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var directoriesToEnsure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(inputPath))
            {
                AddFileToBatch(currentRelativePath, inputPath, batches);
                continue;
            }

            if (!Directory.Exists(inputPath))
            {
                continue;
            }

            var rootFolderName = Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rootFolderName))
            {
                continue;
            }

            var remoteRootPath = CombineRelativePath(currentRelativePath, rootFolderName);
            directoriesToEnsure.Add(remoteRootPath);

            foreach (var directoryPath in Directory.EnumerateDirectories(inputPath, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = Path.GetRelativePath(inputPath, directoryPath).Replace('\\', '/');
                directoriesToEnsure.Add(CombineRelativePath(remoteRootPath, relativeDirectory));
            }

            foreach (var filePath in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(inputPath, filePath))?.Replace('\\', '/')
                    ?? string.Empty;
                var targetPath = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? remoteRootPath
                    : CombineRelativePath(remoteRootPath, relativeDirectory);
                AddFileToBatch(targetPath, filePath, batches);
            }
        }

        return new UploadPlan(
            batches
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new UploadBatch(item.Key, item.Value))
                .ToArray(),
            directoriesToEnsure.OrderBy(path => path.Count(ch => ch == '/')).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void AddFileToBatch(string targetRelativePath, string filePath, IDictionary<string, List<string>> batches)
    {
        if (!batches.TryGetValue(targetRelativePath, out var files))
        {
            files = new List<string>();
            batches[targetRelativePath] = files;
        }

        files.Add(filePath);
    }

    private async Task<HashSet<string>> CollectDuplicateFilesAsync(DiscoveredServer server, UploadPlan uploadPlan, CancellationToken cancellationToken)
    {
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in uploadPlan.Batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remoteEntries = await _fileShareClientService.BrowseAsync(server, batch.TargetRelativePath, cancellationToken);
            var remoteNames = remoteEntries.Entries
                .Select(entry => entry.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in batch.FilePaths)
            {
                var fileName = Path.GetFileName(filePath);
                if (remoteNames.Contains(fileName))
                {
                    duplicates.Add(BuildRemoteFilePath(batch.TargetRelativePath, fileName));
                }
            }
        }

        return duplicates;
    }

    private async Task EnsureRemoteDirectoryExistsAsync(DiscoveredServer server, string directoryPath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRelativePath(directoryPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        var parentPath = GetParentRelativePath(normalizedPath);
        var folderName = normalizedPath.Split('/').Last();
        var parentEntries = await _fileShareClientService.BrowseAsync(server, parentPath, cancellationToken);
        var existingEntry = parentEntries.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, folderName, StringComparison.OrdinalIgnoreCase));

        if (existingEntry is not null)
        {
            if (!existingEntry.IsDirectory)
            {
                throw new InvalidOperationException($"目标位置已存在同名文件：{BuildRemoteFilePath(parentPath, folderName)}");
            }

            return;
        }

        await _fileShareClientService.CreateFolderAsync(server, parentPath, folderName, cancellationToken);
    }

    private void CancelTransfer()
    {
        _transferCancellationTokenSource?.Cancel();
    }

    private void DisposeTransferCancellationTokenSource()
    {
        _transferCancellationTokenSource?.Dispose();
        _transferCancellationTokenSource = null;
    }

    private static string GetParentRelativePath(string path)
    {
        var normalized = NormalizeRelativePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

    private static string CombineRelativePath(string left, string right)
    {
        var normalizedLeft = NormalizeRelativePath(left);
        var normalizedRight = NormalizeRelativePath(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft))
        {
            return normalizedRight;
        }

        if (string.IsNullOrWhiteSpace(normalizedRight))
        {
            return normalizedLeft;
        }

        return $"{normalizedLeft}/{normalizedRight}";
    }

    private static string BuildRemoteFilePath(string targetRelativePath, string fileName)
    {
        return string.IsNullOrWhiteSpace(targetRelativePath)
            ? fileName
            : $"{NormalizeRelativePath(targetRelativePath)}/{fileName}";
    }

    private static string BuildUploadSelectionSummary(int fileCount, int folderCount)
    {
        if (fileCount > 0 && folderCount > 0)
        {
            return $"{fileCount} 个文件和 {folderCount} 个文件夹";
        }

        if (folderCount > 0)
        {
            return $"{folderCount} 个文件夹";
        }

        return $"{fileCount} 个文件";
    }

    private string BuildBuiltInServerAddress()
    {
        EnsureClientConfigDefaults();
        var host = string.IsNullOrWhiteSpace(_config.Client.BuiltInServerHost)
            ? "192.168.202.163"
            : _config.Client.BuiltInServerHost.Trim();
        var port = _config.Client.BuiltInServerPort > 0
            ? _config.Client.BuiltInServerPort
            : (_config.Server.ServicePort > 0 ? _config.Server.ServicePort : 49443);

        return $"http://{host}:{port}/";
    }

    private void EnsureClientConfigDefaults()
    {
        _config.Client ??= new ClientConfig();
        _config.Discovery ??= new DiscoveryConfig();
        _config.Server ??= new ServerConfig();

        if (string.IsNullOrWhiteSpace(_config.Client.BuiltInServerHost))
        {
            _config.Client.BuiltInServerHost = "192.168.202.163";
        }

        if (_config.Client.BuiltInServerPort <= 0)
        {
            _config.Client.BuiltInServerPort = _config.Server.ServicePort > 0 ? _config.Server.ServicePort : 49443;
        }

        if (string.IsNullOrWhiteSpace(_config.Client.DownloadFolder) ||
            string.Equals(_config.Client.DownloadFolder, GetLegacyDefaultDownloadFolder(), StringComparison.OrdinalIgnoreCase))
        {
            _config.Client.DownloadFolder = GetDefaultDownloadFolder();
        }
    }

    private static string GetDefaultDownloadFolder()
    {
        var dDrive = DriveInfo.GetDrives()
            .FirstOrDefault(drive =>
                drive.IsReady &&
                string.Equals(drive.Name, @"D:\", StringComparison.OrdinalIgnoreCase));

        if (dDrive is not null)
        {
            return Path.Combine(dDrive.RootDirectory.FullName, "LanShare");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LanShare");
    }

    private static string GetLegacyDefaultDownloadFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LanShare");
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private sealed record ManualServerTarget(
        DiscoveredServer Server,
        string InitialRelativePath,
        string DisplayAddress);

    private sealed record UploadBatch(
        string TargetRelativePath,
        IReadOnlyList<string> FilePaths);

    private sealed record UploadPlan(
        IReadOnlyList<UploadBatch> Batches,
        IReadOnlyList<string> DirectoriesToEnsure)
    {
        public int FileCount => Batches.Sum(batch => batch.FilePaths.Count);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
