using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GamepadNav.Core.Native;
using GamepadNav.Service;

namespace GamepadNav.App;

/// <summary>
/// Read-only, controller-triggered overlay showing the current button mapping and
/// GamepadNav's live status (active / disabled / game-paused / suppressed). Toggled by
/// Back+B. Designed to avoid the focus-stealing and clutter issues of the keyboard
/// overlay: it never takes keyboard focus (WS_EX_NOACTIVATE + ShowActivated=false),
/// uses large high-contrast text for at-a-glance couch reading, and centers itself on
/// whichever monitor currently has the foreground window instead of always the primary.
/// </summary>
public partial class ComboViewerWindow : Window
{
    // Single source of truth for the cheat-sheet content — keep in sync with
    // ButtonHandler.cs / InputEngine.cs if the actual mappings change.
    private static readonly (string Button, string Action)[] Mappings =
    [
        ("Left Stick", "Move cursor"),
        ("Right Stick", "Scroll"),
        ("RT", "Left click"),
        ("LT", "Right click"),
        ("RB (hold)", "Alt  (RB+X = Alt+Tab)"),
        ("LB (hold)", "Ctrl  (LB+A = Ctrl+Enter)"),
        ("A", "Enter"),
        ("B", "Backspace"),
        ("X", "Tab"),
        ("Y", "Windows / Start menu"),
        ("D-Pad", "Arrow keys"),
        ("Start", "Escape"),
        ("Back + Y", "Toggle keyboard overlay"),
        ("Back + X", "Toggle numpad overlay"),
        ("Back + B", "Toggle this panel"),
        ("L3 + R3", "Enable / disable GamepadNav"),
    ];

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public ComboViewerWindow()
    {
        InitializeComponent();
        BuildRows();

        // Create the native window handle eagerly (without showing) so the panel
        // is instant on first Back+B press instead of paying WPF init cost then.
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Belt-and-suspenders against focus stealing: WPF's ShowActivated=false has a
        // known quirk where it's only honored on the very first Show() call in some
        // versions. Setting WS_EX_NOACTIVATE at the Win32 level guarantees this panel
        // never takes focus away from a game or whatever the user was doing, on every
        // Show()/Hide() cycle.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        RefreshStatus();
        PositionOnActiveMonitor();
        Show();
    }

    private void RefreshStatus()
    {
        if (InputEngine.StatusFullSuppressed)
        {
            SetStatus("#F44336", "GamepadNav Suppressed");
        }
        else if (!InputEngine.StatusEnabled)
        {
            SetStatus("#F44336", "GamepadNav Disabled");
        }
        else if (InputEngine.StatusGameBlocked)
        {
            string game = InputEngine.StatusGameName ?? "game";
            SetStatus("#FFC107", $"Input Paused — {game} in foreground");
        }
        else
        {
            SetStatus("#4CAF50", "GamepadNav Active");
        }
    }

    private void SetStatus(string colorHex, string text)
    {
        StatusDot.Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!;
        StatusText.Text = text;
    }

    /// <summary>
    /// Centers the panel on the monitor currently hosting the foreground window rather
    /// than always the primary display, so it doesn't pop up on the wrong screen in a
    /// multi-monitor setup. Note: this uses the DPI already assigned to this window
    /// (from its last known position), not necessarily the target monitor's DPI — a
    /// full fix would need to handle WM_DPICHANGED, which isn't worth the complexity
    /// unless monitors with different scale factors are actually in use.
    /// </summary>
    private void PositionOnActiveMonitor()
    {
        var hwnd = InputApi.GetForegroundWindow();
        var monitor = hwnd != nint.Zero
            ? InputApi.MonitorFromWindow(hwnd, InputApi.MONITOR_DEFAULTTONEAREST)
            : nint.Zero;

        InputApi.RECT workArea;
        if (monitor != nint.Zero)
        {
            var mi = new InputApi.MONITORINFO { cbSize = (uint)Marshal.SizeOf<InputApi.MONITORINFO>() };
            InputApi.GetMonitorInfo(monitor, ref mi);
            workArea = mi.rcWork;
        }
        else
        {
            workArea = new InputApi.RECT
            {
                Left = 0,
                Top = 0,
                Right = InputApi.GetSystemMetrics(InputApi.SM_CXSCREEN),
                Bottom = InputApi.GetSystemMetrics(InputApi.SM_CYSCREEN),
            };
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double workLeft = workArea.Left / dpi.DpiScaleX;
        double workTop = workArea.Top / dpi.DpiScaleY;
        double workWidth = (workArea.Right - workArea.Left) / dpi.DpiScaleX;
        double workHeight = (workArea.Bottom - workArea.Top) / dpi.DpiScaleY;

        Left = workLeft + (workWidth - Width) / 2;
        Top = workTop + (workHeight - Height) / 2;
    }

    private void BuildRows()
    {
        int half = (Mappings.Length + 1) / 2;
        for (int i = 0; i < Mappings.Length; i++)
        {
            var target = i < half ? LeftColumn : RightColumn;
            target.Children.Add(BuildRow(Mappings[i].Button, Mappings[i].Action));
        }
    }

    private static UIElement BuildRow(string button, string action)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 16, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 50, 60, 90)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        chip.Child = new TextBlock
        {
            Text = button,
            Foreground = Brushes.White,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"),
        };
        Grid.SetColumn(chip, 0);

        var actionText = new TextBlock
        {
            Text = action,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 230)),
            FontSize = 17,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(actionText, 1);

        row.Children.Add(chip);
        row.Children.Add(actionText);
        return row;
    }
}
