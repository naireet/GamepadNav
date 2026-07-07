using System.Windows;
using H.NotifyIcon;
using GamepadNav.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GamepadNav.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private KeyboardWindow? _keyboardWindow;
    private ComboViewerWindow? _comboViewerWindow;
    private IHost? _engineHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _keyboardWindow = new KeyboardWindow();
        _comboViewerWindow = new ComboViewerWindow();

        // Wire InputEngine overlay commands directly to the keyboard window
        InputEngine.OverlayCallback = action =>
        {
            _keyboardWindow.Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "toggleKeyboard": _keyboardWindow.Toggle(); break;
                    case "toggleNumpad": _keyboardWindow.ToggleNumpad(); break;
                    case "toggleComboViewer": _comboViewerWindow.Toggle(); break;
                }
            });
        };

        // Host InputEngine as a BackgroundService in this process
        _engineHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddHostedService<InputEngine>())
            .Build();
        _ = _engineHost.RunAsync();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "GamepadNav",
            Icon = LoadIcon(),
            ContextMenu = BuildContextMenu(),
        };
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var keyboardItem = new System.Windows.Controls.MenuItem { Header = "Show Keyboard" };
        keyboardItem.Click += (_, _) => _keyboardWindow?.Toggle();
        menu.Items.Add(keyboardItem);

        var shortcutsItem = new System.Windows.Controls.MenuItem { Header = "Show Shortcuts" };
        shortcutsItem.Click += (_, _) => _comboViewerWindow?.Toggle();
        menu.Items.Add(shortcutsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _engineHost?.StopAsync().Wait(TimeSpan.FromSeconds(3));
            _trayIcon?.Dispose();
            Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        return System.Drawing.SystemIcons.Application;
    }
}

