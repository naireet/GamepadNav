using System.Windows;
using H.NotifyIcon;

namespace GamepadNav.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private IpcClient? _ipcClient;
    private KeyboardWindow? _keyboardWindow;
    private ControllerPoller? _poller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _ipcClient = new IpcClient();
        _ipcClient.StatusReceived += OnStatusReceived;
        _ipcClient.Start();

        _keyboardWindow = new KeyboardWindow();

        _poller = new ControllerPoller(_keyboardWindow, _ipcClient);
        _poller.Start();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "GamepadNav",
            Icon = LoadIcon(),
            ContextMenu = BuildContextMenu(),
        };
    }

    private void OnStatusReceived(Core.StatusMessage status)
    {
        Dispatcher.Invoke(() =>
        {
            var suffix = status.GameDetected ? $" (game: {status.CurrentGame})" : "";
            var state = status.Enabled ? "Enabled" : "Disabled";
            _trayIcon!.ToolTipText = $"GamepadNav — {state}{suffix}";
        });
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem { Header = "Toggle Enable" };
        toggleItem.Click += (_, _) => _ipcClient?.SendCommand("toggle");
        menu.Items.Add(toggleItem);

        var keyboardItem = new System.Windows.Controls.MenuItem { Header = "Show Keyboard" };
        keyboardItem.Click += (_, _) => _keyboardWindow?.Toggle();
        menu.Items.Add(keyboardItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _poller?.Stop();
            _ipcClient?.Dispose();
            _trayIcon?.Dispose();
            Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        // Use a system icon as placeholder — replace with custom icon later
        return System.Drawing.SystemIcons.Application;
    }
}

