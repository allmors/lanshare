using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using LanShare.Infrastructure;
using LanShare.Models;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using LanShare.Services.Server;

namespace LanShare.ViewModels;

public sealed class ServerViewModel : BindableBase
{
    private readonly AppConfig _config;
    private readonly IConfigurationService _configurationService;
    private readonly IFileShareServerService _fileShareServerService;
    private readonly IServiceDiscoveryBroadcaster _discoveryBroadcaster;
    private readonly Action<string> _statusCallback;
    private readonly DispatcherTimer _connectedClientsTimer;

    private string _serverStatus = "服务未启动";
    private string _serverEndpoint = "尚未监听";
    private bool _isServerRunning;
    private PermissionRule? _selectedPermissionRule;
    private PermissionRule? _editingPermissionRule;
    private string _selectedRuleUserName = "guest";
    private string _ruleDirectoryPath = string.Empty;
    private PermissionRuleEffect _selectedRuleEffect = PermissionRuleEffect.Allow;
    private bool _ruleRead = true;
    private bool _ruleWrite;
    private bool _ruleDelete;
    private bool _ruleInheritToChildren = true;
    private SharedPathItem? _selectedSharedPath;
    private string _sharedPathSearchText = string.Empty;
    private string _currentSharedBrowsePath = string.Empty;
    private List<SharedPathItem> _currentSharedPathEntries = [];

    public ServerViewModel(
        AppConfig config,
        IConfigurationService configurationService,
        IFileShareServerService fileShareServerService,
        IServiceDiscoveryBroadcaster discoveryBroadcaster,
        Action<string> statusCallback)
    {
        _config = config;
        _configurationService = configurationService;
        _fileShareServerService = fileShareServerService;
        _discoveryBroadcaster = discoveryBroadcaster;
        _statusCallback = statusCallback;

        BrowseFolderCommand = new RelayCommand(BrowseFolder, () => !IsServerRunning);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, CanStartServer);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => IsServerRunning);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        SavePermissionRuleCommand = new RelayCommand(SavePermissionRule, CanSavePermissionRule);
        RemovePermissionRuleCommand = new RelayCommand(RemovePermissionRule, () => SelectedPermissionRule is not null);
        LoadSelectedRuleCommand = new RelayCommand(LoadSelectedRuleIntoEditor, () => SelectedPermissionRule is not null);
        ResetPermissionEditorCommand = new RelayCommand(ResetPermissionEditor);
        RefreshSharedPathsCommand = new RelayCommand(RefreshSharedPaths);
        NavigateUpSharedPathCommand = new RelayCommand(NavigateUpSharedPath, () => CanNavigateUpSharedPath);
        OpenSelectedSharedPathCommand = new RelayCommand(OpenSelectedSharedPath, () => SelectedSharedPath is { IsDirectory: true });

        Users = new ObservableCollection<UserAccount>(_config.Permissions.Users);
        PermissionRules = new ObservableCollection<PermissionRule>();
        SharedPaths = new ObservableCollection<SharedPathItem>();
        ConnectedClients = new ObservableCollection<ConnectedClientInfo>();
        RuleEffects = Enum.GetValues<PermissionRuleEffect>();

        _connectedClientsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _connectedClientsTimer.Tick += (_, _) => RefreshConnectedClients();

        RefreshPermissionRules();
        RefreshSharedPaths();
        RefreshConnectedClients();
        ResetPermissionEditor();
    }

    public ObservableCollection<UserAccount> Users { get; }

    public ObservableCollection<PermissionRule> PermissionRules { get; }

    public ObservableCollection<SharedPathItem> SharedPaths { get; }

    public ObservableCollection<ConnectedClientInfo> ConnectedClients { get; }

    public PermissionRuleEffect[] RuleEffects { get; }

    public string ServerName
    {
        get => _config.Server.ServerName;
        set
        {
            if (_config.Server.ServerName != value)
            {
                _config.Server.ServerName = value;
                RaisePropertyChanged();
                RaiseServerCommandStates();
            }
        }
    }

    public string SharedFolderPath
    {
        get => _config.Server.SharedFolderPath;
        set
        {
            if (_config.Server.SharedFolderPath != value)
            {
                _config.Server.SharedFolderPath = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SharedPathPreviewTitle));
                RefreshSharedPaths();
                RaiseServerCommandStates();
            }
        }
    }

    public int ServicePort
    {
        get => _config.Server.ServicePort;
        set
        {
            if (_config.Server.ServicePort != value)
            {
                _config.Server.ServicePort = value;
                RaisePropertyChanged();
                RaiseServerCommandStates();
            }
        }
    }

    public int DiscoveryPort
    {
        get => _config.Discovery.DiscoveryPort;
        set
        {
            if (_config.Discovery.DiscoveryPort != value)
            {
                _config.Discovery.DiscoveryPort = value;
                RaisePropertyChanged();
                RaiseServerCommandStates();
            }
        }
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        private set
        {
            if (SetProperty(ref _isServerRunning, value))
            {
                RaiseServerCommandStates();
            }
        }
    }

    public bool IsEditingRule => _editingPermissionRule is not null;

    public string PermissionEditorTitle => IsEditingRule ? "编辑规则" : "新增规则";

    public string SavePermissionRuleButtonText => IsEditingRule ? "保存修改" : "追加规则";

    public string SharedPathPreviewTitle => string.IsNullOrWhiteSpace(SharedFolderPath)
        ? "路径预览"
        : $"路径预览：{SharedFolderPath}";

    public string CurrentSharedBrowseDisplay => string.IsNullOrWhiteSpace(CurrentSharedBrowsePath) ? "/" : $"/{CurrentSharedBrowsePath}";

    public string ServerStatus
    {
        get => _serverStatus;
        private set => SetProperty(ref _serverStatus, value);
    }

    public string ServerEndpoint
    {
        get => _serverEndpoint;
        private set => SetProperty(ref _serverEndpoint, value);
    }

    public PermissionRule? SelectedPermissionRule
    {
        get => _selectedPermissionRule;
        set
        {
            if (SetProperty(ref _selectedPermissionRule, value))
            {
                RemovePermissionRuleCommand.RaiseCanExecuteChanged();
                LoadSelectedRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public SharedPathItem? SelectedSharedPath
    {
        get => _selectedSharedPath;
        set
        {
            if (SetProperty(ref _selectedSharedPath, value))
            {
                RuleDirectoryPath = value?.RelativePath ?? string.Empty;
                OpenSelectedSharedPathCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedRuleUserName
    {
        get => _selectedRuleUserName;
        set
        {
            if (SetProperty(ref _selectedRuleUserName, value))
            {
                SavePermissionRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RuleDirectoryPath
    {
        get => _ruleDirectoryPath;
        set
        {
            var normalizedPath = NormalizeRelativePath(value);
            if (SetProperty(ref _ruleDirectoryPath, normalizedPath))
            {
                SyncPathSelections(normalizedPath);
            }
        }
    }

    public PermissionRuleEffect SelectedRuleEffect
    {
        get => _selectedRuleEffect;
        set => SetProperty(ref _selectedRuleEffect, value);
    }

    public bool RuleRead
    {
        get => _ruleRead;
        set
        {
            if (SetProperty(ref _ruleRead, value))
            {
                SavePermissionRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool RuleWrite
    {
        get => _ruleWrite;
        set
        {
            if (SetProperty(ref _ruleWrite, value))
            {
                SavePermissionRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool RuleDelete
    {
        get => _ruleDelete;
        set
        {
            if (SetProperty(ref _ruleDelete, value))
            {
                SavePermissionRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool RuleInheritToChildren
    {
        get => _ruleInheritToChildren;
        set => SetProperty(ref _ruleInheritToChildren, value);
    }

    public string SharedPathSearchText
    {
        get => _sharedPathSearchText;
        set
        {
            if (SetProperty(ref _sharedPathSearchText, value))
            {
                ApplySharedPathFilter();
            }
        }
    }

    public string CurrentSharedBrowsePath
    {
        get => _currentSharedBrowsePath;
        private set
        {
            if (SetProperty(ref _currentSharedBrowsePath, value))
            {
                RaisePropertyChanged(nameof(CurrentSharedBrowseDisplay));
                RaisePropertyChanged(nameof(CanNavigateUpSharedPath));
                NavigateUpSharedPathCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanNavigateUpSharedPath => !string.IsNullOrWhiteSpace(CurrentSharedBrowsePath);

    public RelayCommand BrowseFolderCommand { get; }

    public AsyncRelayCommand StartServerCommand { get; }

    public AsyncRelayCommand StopServerCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand SavePermissionRuleCommand { get; }

    public RelayCommand RemovePermissionRuleCommand { get; }

    public RelayCommand LoadSelectedRuleCommand { get; }

    public RelayCommand ResetPermissionEditorCommand { get; }

    public RelayCommand RefreshSharedPathsCommand { get; }

    public RelayCommand NavigateUpSharedPathCommand { get; }

    public RelayCommand OpenSelectedSharedPathCommand { get; }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择需要共享的目录",
            ShowNewFolderButton = true,
            SelectedPath = SharedFolderPath
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SharedFolderPath = dialog.SelectedPath;
            _statusCallback($"已选择共享目录：{dialog.SelectedPath}");
        }
    }

    private async Task StartServerAsync()
    {
        var options = new ServerStartOptions
        {
            ServerName = ServerName,
            SharedFolderPath = SharedFolderPath,
            ServicePort = ServicePort,
            DiscoveryPort = DiscoveryPort,
            BroadcastIntervalSeconds = _config.Discovery.BroadcastIntervalSeconds,
            Permissions = _config.Permissions
        };

        await _fileShareServerService.StartAsync(options);
        await _discoveryBroadcaster.StartAsync(options);

        IsServerRunning = true;
        ServerStatus = "服务运行中";
        ServerEndpoint = _fileShareServerService.BaseAddress ?? "未知地址";
        RefreshConnectedClients();
        _connectedClientsTimer.Start();
        _statusCallback("文件服务和 UDP 广播已启动。");
    }

    private async Task StopServerAsync()
    {
        _connectedClientsTimer.Stop();
        await _discoveryBroadcaster.StopAsync();
        await _fileShareServerService.StopAsync();

        IsServerRunning = false;
        ServerStatus = "服务已停止";
        ServerEndpoint = "尚未监听";
        RefreshConnectedClients();
        _statusCallback("服务端已停止。");
    }

    private void SaveConfig()
    {
        SyncPermissionRulesToConfig();
        _configurationService.Save(_config);
        _statusCallback("配置和权限规则已保存。");
    }

    private bool CanStartServer()
    {
        return !IsServerRunning
            && !string.IsNullOrWhiteSpace(ServerName)
            && !string.IsNullOrWhiteSpace(SharedFolderPath)
            && Directory.Exists(SharedFolderPath)
            && ServicePort > 0
            && DiscoveryPort > 0;
    }

    private bool CanSavePermissionRule()
    {
        return !string.IsNullOrWhiteSpace(SelectedRuleUserName)
            && BuildPermissionsFromEditor() != FilePermission.None;
    }

    private void SavePermissionRule()
    {
        var rule = new PermissionRule
        {
            UserName = SelectedRuleUserName.Trim(),
            DirectoryPath = NormalizeRelativePath(RuleDirectoryPath),
            Effect = SelectedRuleEffect,
            Permissions = BuildPermissionsFromEditor(),
            InheritToChildren = RuleInheritToChildren
        };

        if (HasDuplicateRule(rule))
        {
            _statusCallback("已存在完全相同的权限规则，无需重复添加。");
            return;
        }

        if (_editingPermissionRule is not null)
        {
            var index = PermissionRules.IndexOf(_editingPermissionRule);
            if (index >= 0)
            {
                PermissionRules[index] = rule;
                RefreshPermissionRules();
                SyncPermissionRulesToConfig();
                ResetPermissionEditor();
                _statusCallback($"已更新 {rule.UserName} 在 {rule.DirectoryLabel} 的权限规则。");
                return;
            }
        }

        PermissionRules.Add(rule);
        RefreshPermissionRules();
        SyncPermissionRulesToConfig();
        ResetPermissionEditor();
        _statusCallback($"已新增 {rule.UserName} 在 {rule.DirectoryLabel} 的权限规则。");
    }

    private void RemovePermissionRule()
    {
        if (SelectedPermissionRule is null)
        {
            return;
        }

        var removed = SelectedPermissionRule;
        PermissionRules.Remove(SelectedPermissionRule);
        SyncPermissionRulesToConfig();
        RefreshPermissionRules();
        ResetPermissionEditor();
        _statusCallback($"已删除 {removed.UserName} 在 {removed.DirectoryLabel} 的规则。");
    }

    private void ResetPermissionEditor()
    {
        _editingPermissionRule = null;
        _selectedPermissionRule = null;
        _selectedSharedPath = null;
        RaisePropertyChanged(nameof(SelectedPermissionRule));
        RaisePropertyChanged(nameof(SelectedSharedPath));
        RaisePropertyChanged(nameof(IsEditingRule));
        RaisePropertyChanged(nameof(PermissionEditorTitle));
        RaisePropertyChanged(nameof(SavePermissionRuleButtonText));
        RemovePermissionRuleCommand.RaiseCanExecuteChanged();
        LoadSelectedRuleCommand.RaiseCanExecuteChanged();
        OpenSelectedSharedPathCommand.RaiseCanExecuteChanged();

        SelectedRuleUserName = Users.FirstOrDefault(user => user.IsEnabled)?.UserName ?? "guest";
        RuleDirectoryPath = string.Empty;
        SharedPathSearchText = string.Empty;
        SelectedRuleEffect = PermissionRuleEffect.Allow;
        RuleRead = true;
        RuleWrite = false;
        RuleDelete = false;
        RuleInheritToChildren = true;
        SavePermissionRuleCommand.RaiseCanExecuteChanged();
    }

    private void LoadSelectedRuleIntoEditor()
    {
        if (SelectedPermissionRule is null)
        {
            return;
        }

        _editingPermissionRule = SelectedPermissionRule;
        RaisePropertyChanged(nameof(IsEditingRule));
        RaisePropertyChanged(nameof(PermissionEditorTitle));
        RaisePropertyChanged(nameof(SavePermissionRuleButtonText));

        SelectedRuleUserName = SelectedPermissionRule.UserName;
        RuleDirectoryPath = SelectedPermissionRule.DirectoryPath;
        SelectedRuleEffect = SelectedPermissionRule.Effect;
        RuleRead = (SelectedPermissionRule.Permissions & FilePermission.Read) == FilePermission.Read;
        RuleWrite = (SelectedPermissionRule.Permissions & FilePermission.Write) == FilePermission.Write;
        RuleDelete = (SelectedPermissionRule.Permissions & FilePermission.Delete) == FilePermission.Delete;
        RuleInheritToChildren = SelectedPermissionRule.InheritToChildren;
        OpenSharedBrowserToPath(GetParentRelativePath(SelectedPermissionRule.DirectoryPath));
        SelectedSharedPath = SharedPaths.FirstOrDefault(item =>
            string.Equals(item.RelativePath, SelectedPermissionRule.DirectoryPath, StringComparison.OrdinalIgnoreCase));

        _statusCallback("已加载选中规则，保存时只会替换当前这一条规则。");
    }

    private void RefreshSharedPaths()
    {
        if (string.IsNullOrWhiteSpace(SharedFolderPath) || !Directory.Exists(SharedFolderPath))
        {
            CurrentSharedBrowsePath = string.Empty;
            _currentSharedPathEntries = [];
            SharedPaths.Clear();
            SelectedSharedPath = null;
            return;
        }

        var targetPath = CurrentSharedBrowsePath;
        if (!TryResolveBrowsePath(targetPath, out _))
        {
            targetPath = string.Empty;
        }

        LoadSharedPathEntries(targetPath);
    }

    private IEnumerable<SharedPathItem> EnumerateCurrentDirectory(string rootPath, string relativePath)
    {
        var currentPhysicalPath = string.IsNullOrWhiteSpace(relativePath)
            ? rootPath
            : Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var directory in Directory.EnumerateDirectories(currentPhysicalPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return new SharedPathItem
            {
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, directory)),
                IsDirectory = true
            };
        }

        foreach (var file in Directory.EnumerateFiles(currentPhysicalPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return new SharedPathItem
            {
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, file)),
                IsDirectory = false
            };
        }
    }

    private FilePermission BuildPermissionsFromEditor()
    {
        var permissions = FilePermission.None;

        if (RuleRead)
        {
            permissions |= FilePermission.Read;
        }

        if (RuleWrite)
        {
            permissions |= FilePermission.Write;
        }

        if (RuleDelete)
        {
            permissions |= FilePermission.Delete;
        }

        return permissions;
    }

    private void RefreshPermissionRules()
    {
        var sourceRules = PermissionRules.Count == 0
            ? _config.Permissions.Rules.ToList()
            : PermissionRules.ToList();

        var orderedRules = sourceRules
            .OrderBy(rule => rule.UserName)
            .ThenBy(rule => string.IsNullOrWhiteSpace(rule.DirectoryPath) ? 0 : rule.DirectoryPath.Split('/', '\\').Length)
            .ThenBy(rule => rule.DirectoryPath)
            .ToList();

        PermissionRules.Clear();
        foreach (var rule in orderedRules)
        {
            PermissionRules.Add(rule);
        }
    }

    private void SyncPermissionRulesToConfig()
    {
        _config.Permissions.Rules = PermissionRules
            .Select(rule => new PermissionRule
            {
                UserName = rule.UserName,
                DirectoryPath = rule.DirectoryPath,
                Effect = rule.Effect,
                Permissions = rule.Permissions,
                InheritToChildren = rule.InheritToChildren
            })
            .ToList();
    }

    private void RefreshConnectedClients()
    {
        var snapshot = _fileShareServerService.GetRecentClientsSnapshot();
        ConnectedClients.Clear();

        foreach (var client in snapshot)
        {
            ConnectedClients.Add(client);
        }
    }

    private void RaiseServerCommandStates()
    {
        BrowseFolderCommand.RaiseCanExecuteChanged();
        StartServerCommand.RaiseCanExecuteChanged();
        StopServerCommand.RaiseCanExecuteChanged();
        NavigateUpSharedPathCommand.RaiseCanExecuteChanged();
        OpenSelectedSharedPathCommand.RaiseCanExecuteChanged();
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').Trim().Trim('/');
    }

    private bool HasDuplicateRule(PermissionRule candidate)
    {
        return PermissionRules.Any(rule =>
        {
            if (_editingPermissionRule is not null && ReferenceEquals(rule, _editingPermissionRule))
            {
                return false;
            }

            return string.Equals(rule.UserName, candidate.UserName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.DirectoryPath, candidate.DirectoryPath, StringComparison.OrdinalIgnoreCase)
                && rule.Effect == candidate.Effect
                && rule.Permissions == candidate.Permissions
                && rule.InheritToChildren == candidate.InheritToChildren;
        });
    }

    private void ApplySharedPathFilter()
    {
        SharedPaths.Clear();

        if (_currentSharedPathEntries.Count == 0)
        {
            return;
        }

        var query = (SharedPathSearchText ?? string.Empty).Trim();
        var matches = _currentSharedPathEntries
            .Where(item => MatchesSharedPath(item, query))
            .OrderBy(item => item.IsDirectory ? 0 : 1)
            .ThenBy(item => GetSharedPathMatchRank(item, query))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var match in matches)
        {
            SharedPaths.Add(match);
        }
    }

    private void SyncPathSelections(string normalizedPath)
    {
        var exactMatch = _currentSharedPathEntries.FirstOrDefault(item =>
            string.Equals(item.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(_selectedSharedPath, exactMatch))
        {
            _selectedSharedPath = exactMatch;
            RaisePropertyChanged(nameof(SelectedSharedPath));
        }
    }

    public void OpenSelectedSharedPath()
    {
        if (SelectedSharedPath is not { IsDirectory: true })
        {
            return;
        }

        OpenSharedBrowserToPath(SelectedSharedPath.RelativePath);
    }

    private void NavigateUpSharedPath()
    {
        OpenSharedBrowserToPath(GetParentRelativePath(CurrentSharedBrowsePath));
    }

    private void OpenSharedBrowserToPath(string relativePath)
    {
        LoadSharedPathEntries(relativePath);
    }

    private void LoadSharedPathEntries(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(SharedFolderPath) || !Directory.Exists(SharedFolderPath))
        {
            return;
        }

        var normalizedPath = NormalizeRelativePath(relativePath);
        if (!TryResolveBrowsePath(normalizedPath, out _))
        {
            return;
        }

        CurrentSharedBrowsePath = normalizedPath;
        _currentSharedPathEntries = EnumerateCurrentDirectory(SharedFolderPath, normalizedPath).ToList();
        ApplySharedPathFilter();

        if (!string.IsNullOrWhiteSpace(RuleDirectoryPath))
        {
            SelectedSharedPath = SharedPaths.FirstOrDefault(item =>
                string.Equals(item.RelativePath, RuleDirectoryPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedSharedPath = null;
        }
    }

    private bool TryResolveBrowsePath(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(SharedFolderPath) || !Directory.Exists(SharedFolderPath))
        {
            return false;
        }

        var rootPath = Path.GetFullPath(SharedFolderPath);
        var combinedPath = string.IsNullOrWhiteSpace(relativePath)
            ? rootPath
            : Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!combinedPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(combinedPath))
        {
            return false;
        }

        fullPath = combinedPath;
        return true;
    }

    private static bool MatchesSharedPath(SharedPathItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.DisplayPath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSharedPathMatchRank(SharedPathItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        if (string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (item.DisplayPath.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (item.DisplayPath.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
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
}
