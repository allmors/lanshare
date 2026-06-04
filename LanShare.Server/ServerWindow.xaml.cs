using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using LanShare.ViewModels;
using Forms = System.Windows.Forms;

namespace LanShare.ServerApp;

public partial class ServerWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _isExiting;

    public ServerWindow()
    {
        InitializeComponent();

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开服务端", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());

        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LanShare.ico");
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = File.Exists(trayIconPath) ? new Icon(trayIconPath) : SystemIcons.Application,
            Text = "局域网共享-服务端",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        Closing += ServerWindow_Closing;
        Closed += ServerWindow_Closed;
    }

    private void ServerWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void ServerWindow_Closed(object? sender, EventArgs e)
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

    private void SharedPathsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ServerViewModel viewModel)
        {
            viewModel.OpenSelectedSharedPath();
        }
    }
}
