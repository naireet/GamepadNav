using System.Runtime.InteropServices;
using GamepadNav.Core;

namespace GamepadNav.Overlay;

/// <summary>
/// Standalone test harness for the numpad overlay.
/// In production, the service creates and drives the overlay directly.
/// </summary>
public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);

    public static void Main()
    {
        Console.WriteLine("Starting numpad overlay...");
        var hInstance = GetModuleHandle(null);
        Console.WriteLine($"hInstance: {hInstance}");
        
        var hwnd = NumpadOverlay.Create(hInstance);
        Console.WriteLine($"hwnd: {hwnd} (error: {Marshal.GetLastWin32Error()})");
        
        if (hwnd == nint.Zero)
        {
            Console.WriteLine("Failed to create overlay window!");
            Console.ReadLine();
            return;
        }

        NumpadOverlay.Show();
        Console.WriteLine("Overlay shown. D-pad=navigate, A=select, B=backspace, Back+Y=exit");

        // Simple XInput poll loop for testing
        var reader = new XInputReader(0.15f);
        var previous = new ControllerState();

        while (true)
        {
            // Process Win32 messages
            while (PeekMessage(out var msg, nint.Zero, 0, 0, 1)) // PM_REMOVE
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            var state = reader.Read();
            if (state.IsConnected)
            {
                // Back+X to toggle, Back+Y to exit
                bool backHeld = state.IsButtonDown(GamepadButtons.Back);
                if (backHeld && Pressed(state, previous, GamepadButtons.X))
                    NumpadOverlay.Toggle();
                if (backHeld && Pressed(state, previous, GamepadButtons.Y))
                    break;

                NumpadOverlay.ProcessInput(state, previous);
            }
            previous = state;
            Thread.Sleep(16);
        }
    }

    private static bool Pressed(ControllerState current, ControllerState previous, GamepadButtons button)
        => current.IsButtonDown(button) && !previous.IsButtonDown(button);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG msg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }
}
