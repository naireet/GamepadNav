using System.Runtime.InteropServices;
using GamepadNav.Core;
using GamepadNav.Core.Native;

namespace GamepadNav.Overlay;

/// <summary>
/// Win32/GDI numpad overlay that works on both Default and Winlogon desktops.
/// Shows a 3x4 numpad grid navigated by d-pad, A=select, B=backspace.
/// Sends keystrokes to the focused window (PIN field on lock screen).
/// </summary>
public static partial class NumpadOverlay
{
    private static nint _hwnd;
    private static bool _visible;
    private static int _selectedRow;
    private static int _selectedCol;
    private static bool _registered;

    // Layout: 3 columns x 4 rows
    private static readonly string[,] Keys = {
        { "1", "2", "3" },
        { "4", "5", "6" },
        { "7", "8", "9" },
        { "⌫", "0", "⏎" },
    };

    private const int Rows = 4;
    private const int Cols = 3;
    private const int CellW = 80;
    private const int CellH = 60;
    private const int Padding = 8;
    private const int HeaderH = 30;

    private static int WinW => Cols * CellW + Padding * 2;
    private static int WinH => Rows * CellH + Padding * 2 + HeaderH;

    // Colors
    private static nint _bgBrush;
    private static nint _cellBrush;
    private static nint _selectedBrush;
    private static nint _textColor;
    private static nint _headerColor;
    private static nint _font;
    private static nint _headerFont;

    private const string ClassName = "GamepadNavNumpad";

    public static nint Create(nint hInstance)
    {
        if (!_registered)
        {
            _bgBrush = CreateSolidBrush(RGB(26, 26, 46));
            _cellBrush = CreateSolidBrush(RGB(40, 40, 60));
            _selectedBrush = CreateSolidBrush(RGB(80, 120, 220));
            _textColor = (nint)RGB(255, 255, 255);
            _headerColor = (nint)RGB(136, 136, 136);

            _font = CreateFontW(24, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
            _headerFont = CreateFontW(13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");

            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = WndProc,
                hInstance = hInstance,
                lpszClassName = ClassName,
                hbrBackground = _bgBrush,
                hCursor = LoadCursorW(nint.Zero, 32512), // IDC_ARROW
            };
            RegisterClassExW(ref wc);
            _registered = true;
        }

        int screenW = InputApi.GetSystemMetrics(InputApi.SM_CXSCREEN);
        int screenH = InputApi.GetSystemMetrics(0x01); // SM_CYSCREEN
        int x = (screenW - WinW) / 2;
        int y = screenH - WinH - 40;

        _hwnd = CreateWindowExW(
            0x00000008 | 0x00080000, // WS_EX_TOPMOST | WS_EX_LAYERED
            ClassName,
            "GamepadNav Numpad",
            0x80000000, // WS_POPUP
            x, y, WinW, WinH,
            nint.Zero, nint.Zero, hInstance, nint.Zero);

        // Set opacity
        SetLayeredWindowAttributes(_hwnd, 0, 230, 0x02); // LWA_ALPHA

        return _hwnd;
    }

    public static void Show()
    {
        if (_hwnd == nint.Zero) return;
        _selectedRow = 0;
        _selectedCol = 0;
        ShowWindow(_hwnd, 5); // SW_SHOW
        InvalidateRect(_hwnd, nint.Zero, true);
        _visible = true;
    }

    public static void Hide()
    {
        if (_hwnd == nint.Zero) return;
        ShowWindow(_hwnd, 0); // SW_HIDE
        _visible = false;
    }

    public static bool IsVisible => _visible;

    public static void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    /// <summary>
    /// Process controller input for numpad navigation. Call from the poll loop.
    /// Returns true if input was consumed (overlay is visible).
    /// </summary>
    public static bool ProcessInput(ControllerState current, ControllerState previous)
    {
        if (!_visible) return false;

        // D-pad navigation
        if (Pressed(current, previous, GamepadButtons.DPadUp))
            _selectedRow = Math.Max(0, _selectedRow - 1);
        if (Pressed(current, previous, GamepadButtons.DPadDown))
            _selectedRow = Math.Min(Rows - 1, _selectedRow + 1);
        if (Pressed(current, previous, GamepadButtons.DPadLeft))
            _selectedCol = Math.Max(0, _selectedCol - 1);
        if (Pressed(current, previous, GamepadButtons.DPadRight))
            _selectedCol = Math.Min(Cols - 1, _selectedCol + 1);

        // A = activate selected key
        if (Pressed(current, previous, GamepadButtons.A))
            ActivateKey(_selectedRow, _selectedCol);

        // B = backspace
        if (Pressed(current, previous, GamepadButtons.B))
            SendVKey(InputApi.VK_BACK);

        InvalidateRect(_hwnd, nint.Zero, true);
        return true;
    }

    private static void ActivateKey(int row, int col)
    {
        string key = Keys[row, col];
        switch (key)
        {
            case "⌫": SendVKey(InputApi.VK_BACK); break;
            case "⏎": SendVKey(InputApi.VK_RETURN); break;
            default:
                if (key.Length == 1 && char.IsDigit(key[0]))
                    SendVKey((ushort)(0x30 + (key[0] - '0'))); // VK_0..VK_9
                break;
        }
    }

    private static void SendVKey(ushort vk)
    {
        var inputs = new InputApi.INPUT[]
        {
            new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = vk } } },
            new() { type = InputApi.INPUT_KEYBOARD, union = new() { ki = new() { wVk = vk, dwFlags = InputApi.KEYEVENTF_KEYUP } } },
        };
        InputApi.SendInput(2, inputs, Marshal.SizeOf<InputApi.INPUT>());
    }

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);

    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == 0x000F) // WM_PAINT
        {
            var ps = new PAINTSTRUCT();
            nint hdc = BeginPaint(hwnd, ref ps);
            Paint(hdc);
            EndPaint(hwnd, ref ps);
            return nint.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Paint(nint hdc)
    {
        // Header
        var oldFont = SelectObject(hdc, _headerFont);
        SetBkMode(hdc, 1); // TRANSPARENT
        SetTextColor(hdc, _headerColor);
        var headerRect = new RECT2 { Left = Padding, Top = 4, Right = WinW - Padding, Bottom = HeaderH };
        DrawTextW(hdc, "D-pad Navigate  |  A Select  |  B Backspace", -1, ref headerRect, 0x01); // DT_CENTER

        // Grid
        SelectObject(hdc, _font);
        SetTextColor(hdc, _textColor);

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int x = Padding + c * CellW + 2;
                int y = HeaderH + Padding + r * CellH + 2;
                int w = CellW - 4;
                int h = CellH - 4;

                var rect = new RECT2 { Left = x, Top = y, Right = x + w, Bottom = y + h };
                var brush = (r == _selectedRow && c == _selectedCol) ? _selectedBrush : _cellBrush;

                // Draw rounded-ish cell (fill rect)
                FillRect(hdc, ref rect, brush);

                // Draw text centered
                DrawTextW(hdc, Keys[r, c], -1, ref rect, 0x01 | 0x04 | 0x20); // CENTER | VCENTER | SINGLELINE
            }
        }

        SelectObject(hdc, oldFont);
    }

    // --- P/Invoke ---

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT ps);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursorW(nint hInstance, int cursor);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeOut, uint charSet,
        uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint hdc, nint color);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawTextW(nint hdc, string text, int count, ref RECT2 rect, uint format);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint hdc, ref RECT2 rect, nint brush);

    private static uint RGB(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT2 rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT2
    {
        public int Left, Top, Right, Bottom;
    }
}
