using System;
using LanShare.Infrastructure;
using LanShare.Models;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using LanShare.Services.Server;

namespace LanShare.ViewModels;

public sealed class MainWindowViewModel : BindableBase
{
    private AppMode _currentMode;
    private bool _isServerAccessUnlocked;
    private string _statusMessage = "等待操作";
    private string _footerMessage = "客户端会优先连接内置服务端，连接失败后再自动发现局域网服务。";

    public MainWindowViewModel(
        AppConfig config,
        IConfigurationService configurationService,
        IFileShareServerService fileShareServerService,
        IServiceDiscoveryBroadcaster discoveryBroadcaster,
        IServiceDiscoveryClient discoveryClient)
    {
        Config = config;
        Server = new ServerViewModel(config, configurationService, fileShareServerService, discoveryBroadcaster, OnStatusChanged);
        Client = new ClientViewModel(config, configurationService, discoveryClient, new Services.Client.FileShareClientService(), OnStatusChanged);
        _currentMode = AppMode.Client;
        Config.StartupMode = AppMode.Client;

        RequestServerAccessCommand = new RelayCommand(RequestServerAccess);
        HideServerAccessCommand = new RelayCommand(HideServerAccess, () => IsServerAccessUnlocked);
    }

    public AppConfig Config { get; }

    public ServerViewModel Server { get; }

    public ClientViewModel Client { get; }

    public bool IsServerAccessUnlocked
    {
        get => _isServerAccessUnlocked;
        private set
        {
            if (SetProperty(ref _isServerAccessUnlocked, value))
            {
                RaisePropertyChanged(nameof(IsServerVisible));
                HideServerAccessCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsServerVisible => IsServerAccessUnlocked;

    public bool IsServerMode
    {
        get => _currentMode == AppMode.Server;
        set
        {
            if (value && IsServerAccessUnlocked)
            {
                CurrentMode = AppMode.Server;
            }
        }
    }

    public bool IsClientMode
    {
        get => _currentMode == AppMode.Client;
        set
        {
            if (value)
            {
                CurrentMode = AppMode.Client;
            }
        }
    }

    public string CurrentModeLabel => _currentMode == AppMode.Server
        ? "当前模式：服务端管理"
        : "当前模式：客户端浏览";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string FooterMessage
    {
        get => _footerMessage;
        private set => SetProperty(ref _footerMessage, value);
    }

    public RelayCommand RequestServerAccessCommand { get; }

    public RelayCommand HideServerAccessCommand { get; }

    private AppMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (SetProperty(ref _currentMode, value))
            {
                Config.StartupMode = value;
                RaisePropertyChanged(nameof(IsServerMode));
                RaisePropertyChanged(nameof(IsClientMode));
                RaisePropertyChanged(nameof(CurrentModeLabel));
            }
        }
    }

    private void RequestServerAccess()
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = new PasswordPromptDialog("管理入口", "请输入管理密钥")
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!string.Equals(dialog.Password, Config.AdminAccessKey, StringComparison.Ordinal))
        {
            OnStatusChanged("管理密钥错误，无法打开服务端功能。");
            System.Windows.MessageBox.Show(
                owner,
                "管理密钥错误。",
                "访问被拒绝",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        IsServerAccessUnlocked = true;
        CurrentMode = AppMode.Server;
        OnStatusChanged("管理密钥验证通过，已打开服务端管理功能。");
    }

    private void HideServerAccess()
    {
        CurrentMode = AppMode.Client;
        IsServerAccessUnlocked = false;
        OnStatusChanged("已隐藏服务端管理功能。");
    }

    private void OnStatusChanged(string message)
    {
        StatusMessage = message;
        FooterMessage = message;
    }
}
