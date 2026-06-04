using System;
using System.Windows;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using LanShare.Services.Server;
using LanShare.ViewModels;

namespace LanShare;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var configPath = AppPaths.GetUserConfigPath();
        var configurationService = new JsonConfigurationService(configPath);
        var appConfig = configurationService.Load();

        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                appConfig,
                configurationService,
                new FileShareServerService(),
                new UdpDiscoveryBroadcaster(),
                new UdpDiscoveryClient())
        };

        mainWindow.Show();
    }
}
