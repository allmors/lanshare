using System;
using System.IO;
using LanShare.Services.Client;
using LanShare.Services.Configuration;
using LanShare.Services.Discovery;
using LanShare.ViewModels;

namespace LanShare.ClientApp;

public partial class App : System.Windows.Application
{
    private static readonly string ErrorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LanShare.Client",
        "client-error.log");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            System.Windows.MessageBox.Show(
                $"客户端启动或运行时出现异常：{args.Exception.Message}",
                "LanShare Client",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogException(exception);
            }
        };

        var configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LanShare.Client");
        var configPath = Path.Combine(configDirectory, "lanshare.client.json");

        var configurationService = new JsonConfigurationService(configPath);
        var appConfig = configurationService.Load();

        var window = new ClientWindow
        {
            DataContext = new ClientViewModel(
                appConfig,
                configurationService,
                new UdpDiscoveryClient(),
                new FileShareClientService(),
                _ => { })
        };

        MainWindow = window;
        window.Show();
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(ErrorLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                ErrorLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
