# GamepadNav

Xbox controller → mouse/keyboard input for Windows. Navigate the desktop, browse the web, enter PINs, and control your PC from the couch — including the login screen.

## Features

- **Mouse emulation** — left stick moves cursor with quadratic acceleration
- **Smooth scrolling** — right stick with exponential smoothing
- **Full button mapping** — triggers for clicks, bumpers for Alt/Ctrl, face buttons for Enter/Backspace/Tab/Win/Esc
- **Combos** — RB+D-pad for browser back/forward, RB+X for Alt+Tab, Back+Y/X for keyboard/numpad overlays
- **Virtual keyboard** — WPF QWERTY + numpad overlay, d-pad navigable
- **Login screen PIN entry** — via [Xbox-PIN-Controller](https://github.com/naireet/Xbox-PIN-Controller) credential provider
- **Game auto-disable** — DLL-based detection (D3D/Vulkan + fullscreen heuristic)
- **Manual toggle** — L3+R3 to enable/disable anytime

## Controller Mapping

| Input | Action |
|-------|--------|
| Left Stick | Mouse cursor |
| Right Stick | Scroll |
| RT / LT | Left click / Right click |
| RB (hold) | Alt — RB+D-pad Left = browser back |
| LB (hold) | Ctrl — LB+A = Ctrl+Enter |
| A | Enter |
| B | Backspace |
| X | Tab — RB+X = Alt+Tab |
| Y | Windows / Start menu |
| D-pad | Arrow keys |
| Start | Escape |
| Back + Y | Toggle keyboard overlay |
| Back + X | Toggle numpad overlay |
| L3 + R3 | Toggle GamepadNav on/off |

## Requirements

- Windows 10/11
- Xbox controller (wireless adapter or Bluetooth)
- .NET 10 Runtime

## Installation

### Desktop Input (Tray App)
```powershell
cd GamepadNav
dotnet publish src\GamepadNav.App -c Release -r win-x64 --self-contained false -o publish\app
```
Runs at logon via scheduled task `GamepadNav`.

### Lock Screen PIN Entry
Uses a [Credential Provider DLL](https://github.com/naireet/Xbox-PIN-Controller) (forked from MikeCoder96, MIT license):

1. Copy `XboxPINController.dll` to `C:\Windows\System32`
2. Run `register.reg` as Administrator
3. Disable built-in controller navigation: set `HKLM\SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping` → `Enabled` = DWORD `0`

To remove: run `Unregister.reg`, delete the DLL.

## Architecture

```
GamepadNav.App          — WPF tray app hosting InputEngine (single process)
GamepadNav.Service      — Standalone engine (for development/testing)
GamepadNav.Core         — Shared types, XInput, P/Invoke, config
GamepadNav.Overlay      — Win32/GDI numpad overlay (for future use)
Xbox-PIN-Controller     — Credential Provider DLL (lock screen, separate repo)
```

## License

MIT
