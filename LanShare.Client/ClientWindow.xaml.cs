using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using LanShare.ViewModels;
using Forms = System.Windows.Forms;

namespace LanShare.ClientApp;

public partial class ClientWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExiting;

    public ClientWindow()
    {
        InitializeComponent();

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开客户端", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());

        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LanShare.ico");
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = File.Exists(trayIconPath) ? new Icon(trayIconPath) : SystemIcons.Application,
            Text = "局域网共享-客户端",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        Loaded += ClientWindow_Loaded;
        Closing += ClientWindow_Closing;
        Closed += ClientWindow_Closed;
        StateChanged += ClientWindow_StateChanged;
    }

    private async void ClientWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ClientViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"客户端初始化失败：{ex.Message}",
                    "局域网共享-客户端",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    private void ServerAddressInputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        if (DataContext is ClientViewModel viewModel && viewModel.ConnectToServerCommand.CanExecute(null))
        {
            viewModel.ConnectToServerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ClientEntriesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ClientViewModel viewModel && viewModel.OpenSelectedDirectoryCommand.CanExecute(null))
        {
            viewModel.OpenSelectedDirectoryCommand.Execute(null);
        }
    }

    private void ClientEntriesDataGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not ClientViewModel viewModel ||
            !viewModel.CanAcceptFileDrop() ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void ClientEntriesDataGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not ClientViewModel viewModel ||
            !viewModel.CanAcceptFileDrop() ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] filePaths || filePaths.Length == 0)
        {
            return;
        }

        try
        {
            await viewModel.UploadFilesFromDropAsync(filePaths);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "拖拽上传失败",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void ClientWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            ShowInTaskbar = true;
        }
    }

    private void ClientWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void ClientWindow_Closed(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _notifyIcon.Visible = false;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
