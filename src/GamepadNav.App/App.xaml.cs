using System.Windows;
using H.NotifyIcon;

namespace GamepadNav.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private IpcClient? _ipcClient;
    private KeyboardWindow? _keyboardWindow;
    private KeyboardController? _keyController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _keyboardWindow = new KeyboardWindow();
        _keyController = new KeyboardController(_keyboardWindow);

        _ipcClient = new IpcClient();
        _ipcClient.StatusReceived += OnStatusReceived;
        _ipcClient.CommandReceived += OnCommandReceived;
        _ipcClient.ControllerStateReceived += OnControllerStateReceived;
        _ipcClient.Start();

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

    private void OnCommandReceived(Core.CommandMessage cmd)
    {
        _keyController?.HandleCommand(cmd.Action);
    }

    private void OnControllerStateReceived(Core.ControllerStateMessage state)
    {
        _keyController?.HandleControllerState(state);
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

