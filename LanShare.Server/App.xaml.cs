using System;
using System.IO;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using LanShare.Services.Server;
using LanShare.ViewModels;

namespace LanShare.ServerApp;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LanShare.Server");
        var configPath = Path.Combine(configDirectory, "lanshare.server.json");

        var configurationService = new JsonConfigurationService(configPath);
        var appConfig = configurationService.Load();

        var window = new ServerWindow
        {
            DataContext = new ServerViewModel(
                appConfig,
                configurationService,
                new FileShareServerService(),
                new UdpDiscoveryBroadcaster(),
                _ => { })
        };

        MainWindow = window;
        window.Show();
    }
}
