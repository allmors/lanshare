using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using LanShare.ViewModels;
using Forms = System.Windows.Forms;

namespace LanShare;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开主窗口", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());

        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LanShare.ico");

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = File.Exists(trayIconPath) ? new Icon(trayIconPath) : SystemIcons.Application,
            Text = "LanShare",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void ServerAddressInputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        if (DataContext is MainWindowViewModel { Client: { } client } &&
            client.ConnectToServerCommand.CanExecute(null))
        {
            client.ConnectToServerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ClientEntriesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Client: { } client } &&
            client.OpenSelectedDirectoryCommand.CanExecute(null))
        {
            client.OpenSelectedDirectoryCommand.Execute(null);
        }
    }

    private void ClientEntriesDataGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Client: { } client } ||
            !client.CanAcceptFileDrop() ||
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
        if (DataContext is not MainWindowViewModel { Client: { } client } ||
            !client.CanAcceptFileDrop() ||
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
            await client.UploadFilesFromDropAsync(filePaths);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "拖拽上传失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SharedPathsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Server: { } server } &&
            server.UseSelectedSharedPathCommand.CanExecute(null))
        {
            server.UseSelectedSharedPathCommand.Execute(null);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
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
