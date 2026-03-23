# GamepadNav

Xbox controller → mouse/keyboard translation for Windows, including login screen support.

## What It Does

Translates Xbox controller input into mouse movement, clicks, scrolling, and keyboard events via a Windows Service. Works on the Windows login/lock screen (Secure Desktop) and the normal user desktop. Auto-disables when games are in the foreground.

## Architecture

```
GamepadNav.sln
├── src/
│   ├── GamepadNav.Core/        # Shared types, config, P/Invoke signatures
│   ├── GamepadNav.Service/     # Windows Service (LocalSystem) — XInput + SendInput engine
│   ├── GamepadNav.App/         # WPF tray app — keyboard overlay, settings, tray icon
│   └── GamepadNav.Overlay/     # Win32/GDI login screen overlay (spawned by service)
```

- **Core**: `ControllerState`, `GamepadNavConfig`, P/Invoke wrappers for XInput, SendInput, Desktop APIs
- **Service**: Polls XInput at 60Hz, translates to SendInput calls, detects active desktop (Winlogon vs Default), monitors foreground window for game detection, named pipe IPC server
- **App**: WPF tray app with virtual keyboard overlay (QWERTY + numpad), settings UI, named pipe IPC client
- **Overlay**: Minimal Win32 app spawned by service on the Winlogon desktop for PIN entry at the login screen

## Build / Run / Test

```powershell
# Build all
dotnet build

# Run service in console mode (dev)
dotnet run --project src\GamepadNav.Service

# Install service
sc.exe create GamepadNav binPath="C:\Code\GamepadNav\src\GamepadNav.Service\bin\Release\net8.0\win-x64\GamepadNav.Service.exe"
sc.exe config GamepadNav start=auto

# Run tray app
dotnet run --project src\GamepadNav.App
```

## Controller Mapping

| Input | Action |
|-------|--------|
| Left Stick | Mouse cursor (acceleration + dead zone) |
| Right Stick | Scroll (vertical + horizontal) |
| RT (Right Trigger) | Left click |
| LT (Left Trigger) | Right click |
| RB | Shift (hold) |
| LB | Ctrl (hold) |
| A | Enter |
| B | Escape / Backspace |
| X | Middle click |
| Y | Windows / Start menu |
| D-pad | Arrow keys |
| Start | (unmapped — reserved) |
| Back | Combo modifier (see below) |
| Back + Y | Toggle virtual keyboard |
| Back + X | Toggle numpad mode |
| L3 + R3 | Toggle GamepadNav on/off |

## Key Files

| File | Purpose |
|------|---------|
| `src/GamepadNav.Core/ControllerState.cs` | Parsed controller state struct |
| `src/GamepadNav.Core/NativeMethods.cs` | All P/Invoke declarations |
| `src/GamepadNav.Core/GamepadNavConfig.cs` | Configuration model |
| `src/GamepadNav.Service/InputEngine.cs` | Main XInput→SendInput translation loop |
| `src/GamepadNav.Service/DesktopManager.cs` | Winlogon/Default desktop switching |
| `src/GamepadNav.Service/GameDetector.cs` | Foreground window game detection |
| `src/GamepadNav.Service/Program.cs` | Service host entry point |

## Tech Stack

- .NET 8, C#
- XInput via P/Invoke (`xinput1_4.dll`)
- SendInput / Desktop APIs via P/Invoke (`user32.dll`, `kernel32.dll`)
- Microsoft.Extensions.Hosting.WindowsServices
- WPF + H.NotifyIcon (tray app)
- System.IO.Pipes (IPC)
