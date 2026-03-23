#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs GamepadNav service and tray app.
.DESCRIPTION
    - Publishes the service as a self-contained executable
    - Creates the Windows Service (GamepadNav)
    - Sets up auto-start
    - Creates default config
    - Adds tray app to user startup
#>

param(
    [string]$InstallDir = "$env:ProgramFiles\GamepadNav",
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$SolutionRoot = Split-Path $PSScriptRoot

if ($Uninstall) {
    Write-Host "Uninstalling GamepadNav..." -ForegroundColor Yellow
    
    $svc = Get-Service -Name GamepadNav -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -eq 'Running') {
            Stop-Service GamepadNav -Force
            Write-Host "  Stopped service"
        }
        sc.exe delete GamepadNav | Out-Null
        Write-Host "  Removed service"
    }

    $startupLink = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\GamepadNav.lnk"
    if (Test-Path $startupLink) {
        Remove-Item $startupLink -Force
        Write-Host "  Removed startup shortcut"
    }

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Host "  Removed $InstallDir"
    }

    Write-Host "GamepadNav uninstalled." -ForegroundColor Green
    return
}

Write-Host "Installing GamepadNav to $InstallDir..." -ForegroundColor Cyan

# Publish service
Write-Host "  Publishing service..."
dotnet publish "$SolutionRoot\src\GamepadNav.Service" `
    -c Release -r win-x64 --self-contained `
    -o "$InstallDir\Service" -p:PublishSingleFile=true --nologo -v q 2>&1 | Out-Null

# Publish tray app
Write-Host "  Publishing tray app..."
dotnet publish "$SolutionRoot\src\GamepadNav.App" `
    -c Release -r win-x64 `
    -o "$InstallDir\App" --nologo -v q 2>&1 | Out-Null

# Create default config if not exists
$configDir = "$env:ProgramData\GamepadNav"
if (-not (Test-Path "$configDir\config.json")) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    @{
        stickDeadZone = 0.15
        cursorSpeed = 15.0
        cursorAcceleration = 2.0
        scrollSpeed = 5.0
        triggerThreshold = 0.3
        controllerIndex = 0
        pollIntervalMs = 16
        gameProcesses = @()
        startEnabled = $true
    } | ConvertTo-Json | Set-Content "$configDir\config.json"
    Write-Host "  Created default config at $configDir\config.json"
}

# Install Windows Service
$servicePath = "$InstallDir\Service\GamepadNav.Service.exe"
$existingSvc = Get-Service -Name GamepadNav -ErrorAction SilentlyContinue
if ($existingSvc) {
    Stop-Service GamepadNav -Force -ErrorAction SilentlyContinue
    sc.exe delete GamepadNav | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create GamepadNav binPath= "`"$servicePath`"" start= auto obj= LocalSystem | Out-Null
sc.exe description GamepadNav "Xbox controller to mouse/keyboard translation for Windows" | Out-Null
Start-Service GamepadNav
Write-Host "  Service installed and started"

# Add tray app to user startup
$startupDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$appPath = "$InstallDir\App\GamepadNav.App.exe"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut("$startupDir\GamepadNav.lnk")
$shortcut.TargetPath = $appPath
$shortcut.WorkingDirectory = "$InstallDir\App"
$shortcut.Description = "GamepadNav Tray App"
$shortcut.Save()
Write-Host "  Tray app added to startup"

Write-Host ""
Write-Host "GamepadNav installed successfully!" -ForegroundColor Green
Write-Host "  Service: running as LocalSystem (auto-start)"
Write-Host "  Tray app: will start on next login (or run manually: $appPath)"
Write-Host "  Config: $configDir\config.json"
