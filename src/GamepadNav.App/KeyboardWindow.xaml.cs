using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GamepadNav.Core;
using GamepadNav.Core.Native;
using System.Runtime.InteropServices;

namespace GamepadNav.App;

public partial class KeyboardWindow : Window
{
    private bool _numpadMode;
    private int _selectedRow;
    private int _selectedCol;
    private int _columns;
    private readonly List<List<KeyDef>> _currentLayout = [];

    // D-pad repeat: initial delay then repeat
    private DateTime _lastDpadTime = DateTime.MinValue;
    private GamepadButtons _lastDpadDirection = GamepadButtons.None;
    private const int DpadInitialDelayMs = 300;
    private const int DpadRepeatDelayMs = 100;

    private static readonly List<List<KeyDef>> QwertyLayout =
    [
        [new("1"), new("2"), new("3"), new("4"), new("5"), new("6"), new("7"), new("8"), new("9"), new("0")],
        [new("Q"), new("W"), new("E"), new("R"), new("T"), new("Y"), new("U"), new("I"), new("O"), new("P")],
        [new("A"), new("S"), new("D"), new("F"), new("G"), new("H"), new("J"), new("K"), new("L"), new("⌫", "BACK")],
        [new("Z"), new("X"), new("C"), new("V"), new("B"), new("N"), new("M"), new(","), new("."), new("⏎", "ENTER")],
        [new("SPACE", "SPACE", 4), new("←", "LEFT"), new("→", "RIGHT"), new("↑", "UP"), new("↓", "DOWN"), new("ESC", "ESC")],
    ];

    private static readonly List<List<KeyDef>> NumpadLayout =
    [
        [new("1"), new("2"), new("3")],
        [new("4"), new("5"), new("6")],
        [new("7"), new("8"), new("9")],
        [new("⌫", "BACK"), new("0"), new("⏎", "ENTER")],
    ];

    public KeyboardWindow()
    {
        InitializeComponent();
        SetLayout(QwertyLayout);
        PositionAtBottom();
        // Allow keyboard focus for Esc handling
        Focusable = true;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            Hide();
    }

    public void Toggle()
    {
        if (IsVisible && !_numpadMode)
            Hide();
        else
        {
            _numpadMode = false;
            SetLayout(QwertyLayout);
            ModeLabel.Text = "KEYBOARD";
            _selectedRow = 0;
            _selectedCol = 0;
            PositionAtBottom();
            if (!IsVisible) Show();
            UpdateHighlight();
        }
    }

    public void ToggleNumpad()
    {
        if (IsVisible && _numpadMode)
            Hide();
        else
        {
            _numpadMode = true;
            SetLayout(NumpadLayout);
            ModeLabel.Text = "NUMPAD";
            _selectedRow = 0;
            _selectedCol = 0;
            PositionAtBottom();
            if (!IsVisible) Show();
            UpdateHighlight();
        }
    }

    public void HandleInput(ControllerState current, ControllerState previous)
    {
        // D-pad navigation with repeat
        HandleDpad(current, GamepadButtons.DPadUp, 0, -1);
        HandleDpad(current, GamepadButtons.DPadDown, 0, 1);
        HandleDpad(current, GamepadButtons.DPadLeft, -1, 0);
        HandleDpad(current, GamepadButtons.DPadRight, 1, 0);

        // A = select key (press)
        if (Pressed(current, previous, GamepadButtons.A))
            ActivateSelected();

        // B = backspace
        if (Pressed(current, previous, GamepadButtons.B))
            SendKey(InputApi.VK_BACK);
    }

    private void HandleDpad(ControllerState current, GamepadButtons direction, int dx, int dy)
    {
        if (!current.IsButtonDown(direction))
        {
            if (_lastDpadDirection == direction)
                _lastDpadDirection = GamepadButtons.None;
            return;
        }

        var now = DateTime.UtcNow;
        bool isNew = _lastDpadDirection != direction;
        int delayMs = isNew ? 0 : (_lastDpadTime == DateTime.MinValue ? DpadInitialDelayMs : DpadRepeatDelayMs);

        if ((now - _lastDpadTime).TotalMilliseconds < delayMs && !isNew) return;

        _lastDpadDirection = direction;
        _lastDpadTime = now;

        if (_currentLayout.Count == 0) return;

        _selectedRow = Math.Clamp(_selectedRow + dy, 0, _currentLayout.Count - 1);
        _selectedCol = Math.Clamp(_selectedCol + dx, 0, _currentLayout[_selectedRow].Count - 1);
        UpdateHighlight();
    }

    private void ActivateSelected()
    {
        if (_selectedRow >= _currentLayout.Count) return;
        var row = _currentLayout[_selectedRow];
        if (_selectedCol >= row.Count) return;

        var key = row[_selectedCol];
        switch (key.Action)
        {
            case "BACK":
                SendKey(InputApi.VK_BACK);
                break;
            case "ENTER":
                SendKey(InputApi.VK_RETURN);
                break;
            case "SPACE":
                SendKey(0x20); // VK_SPACE
                break;
            case "ESC":
                SendKey(InputApi.VK_ESCAPE);
                break;
            case "LEFT":
                SendKey(InputApi.VK_LEFT, extended: true);
                break;
            case "RIGHT":
                SendKey(InputApi.VK_RIGHT, extended: true);
                break;
            case "UP":
                SendKey(InputApi.VK_UP, extended: true);
                break;
            case "DOWN":
                SendKey(InputApi.VK_DOWN, extended: true);
                break;
            default:
                // Type the character
                if (key.Label.Length == 1)
                    SendChar(key.Label[0]);
                break;
        }
    }

    private void SetLayout(List<List<KeyDef>> layout)
    {
        _currentLayout.Clear();
        _currentLayout.AddRange(layout);

        KeyGrid.Children.Clear();
        _columns = _numpadMode ? 3 : 10;
        KeyGrid.Columns = _columns;

        foreach (var row in layout)
        {
            foreach (var key in row)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 60)),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(2),
                    MinWidth = _numpadMode ? 70 : 48,
                    MinHeight = _numpadMode ? 55 : 42,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                if (key.Span > 1)
                {
                    // For multi-span keys, we add empty cells after
                }

                var text = new TextBlock
                {
                    Text = key.Label,
                    Foreground = Brushes.White,
                    FontSize = _numpadMode ? 22 : 16,
                    FontFamily = new FontFamily("Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                border.Child = text;
                KeyGrid.Children.Add(border);
            }
        }
    }

    private void UpdateHighlight()
    {
        int idx = 0;
        for (int r = 0; r < _currentLayout.Count; r++)
        {
            for (int c = 0; c < _currentLayout[r].Count; c++)
            {
                if (idx >= KeyGrid.Children.Count) return;
                var border = (Border)KeyGrid.Children[idx];

                if (r == _selectedRow && c == _selectedCol)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(255, 80, 120, 220));
                    border.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 160, 255));
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(200, 40, 40, 60));
                    border.BorderBrush = null;
                    border.BorderThickness = new Thickness(0);
                }
                idx++;
            }
        }
    }

    private void PositionAtBottom()
    {
        var screen = SystemParameters.WorkArea;
        Width = _numpadMode ? 300 : Math.Min(screen.Width * 0.6, 800);
        Height = _numpadMode ? 320 : 300;
        Left = (screen.Width - Width) / 2;
        Top = screen.Height - Height - 20;
    }

    private static void SendKey(ushort vk, bool extended = false)
    {
        uint flags = extended ? InputApi.KEYEVENTF_EXTENDEDKEY : 0u;
        var inputs = new InputApi.INPUT[]
        {
            new()
            {
                type = InputApi.INPUT_KEYBOARD,
                union = new() { ki = new() { wVk = vk, dwFlags = flags } }
            },
            new()
            {
                type = InputApi.INPUT_KEYBOARD,
                union = new() { ki = new() { wVk = vk, dwFlags = flags | InputApi.KEYEVENTF_KEYUP } }
            }
        };
        InputApi.SendInput(2, inputs, Marshal.SizeOf<InputApi.INPUT>());
    }

    private static void SendChar(char c)
    {
        // Use VkKeyScan to get the virtual key for this character
        short vkResult = VkKeyScan(c);
        if (vkResult == -1) return;

        ushort vk = (ushort)(vkResult & 0xFF);
        bool shift = (vkResult & 0x100) != 0;

        var inputs = new List<InputApi.INPUT>();

        if (shift)
            inputs.Add(new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = InputApi.VK_SHIFT } } });

        inputs.Add(new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = vk } } });
        inputs.Add(new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = vk, dwFlags = InputApi.KEYEVENTF_KEYUP } } });

        if (shift)
            inputs.Add(new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = InputApi.VK_SHIFT, dwFlags = InputApi.KEYEVENTF_KEYUP } } });

        InputApi.SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<InputApi.INPUT>());
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);
}

public record KeyDef(string Label, string? Action = null, int Span = 1)
{
    public KeyDef(string label) : this(label, null, 1) { }
}
