# AGENTS.md

## Project Overview

**SleepMngr** — a C# .NET 8 Windows Forms tray application that prevents a laptop from sleeping when the lid is closed if an external monitor is connected.

## Architecture

### Core Components

- **[Program.cs](Program.cs)** — Entry point. Implements single-instance mutex (`Global\SleepMngr_SingleInstance`). Previous instances are terminated via `Process.GetProcessesByName()`.
- **[TrayApplicationContext.cs](TrayApplicationContext.cs)** — Main application logic. Windows Forms `ApplicationContext` that owns the tray icon, menus, timers, and coordinates all subsystems.
- **[MonitorDetector.cs](MonitorDetector.cs)** — Detects connected monitors via `EnumDisplayMonitors` Win32 API, `Screen.AllScreens`, and WMI (`WmiMonitorID`). Distinguishes built-in vs external displays.
- **[PowerManager.cs](PowerManager.cs)** — Manages sleep prevention via `SetThreadExecutionState` P/Invoke. Provides multiple sleep methods (Modern Standby display off, classic S3 via various APIs). Logs failures to `%AppData%\SleepMngr\sleep_log.txt`.
- **[LidActionManager.cs](LidActionManager.cs)** — Manages lid close action via `powercfg` CLI commands. Can set "Do Nothing" or restore "Sleep". Tracks whether settings were modified.
- **[WakeManager.cs](WakeManager.cs)** — Manages mouse wake capability via `powercfg` device wake settings. Can disable/enable mouse as a wake device.
- **[AutoStartManager.cs](AutoStartManager.cs)** — Manages registry-based autostart under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **[Settings.cs](Settings.cs)** — JSON-based settings persistence at `%AppData%\SleepMngr\settings.json`. Stores user preferences (restore lid settings toggle, etc.).
- **[IconGenerator.cs](IconGenerator.cs)** — Generates colored 16x16 tray icons (blue, yellow, dark blue, dark yellow) for different operating modes.
- **[WorkMode.cs](WorkMode.cs)** — Enum with three values: `Auto`, `AlwaysPrevent`, `AlwaysAllow`.

### Data Flow

1. **Startup**: Mutex check → detect monitors → apply power settings based on mode
2. **Every 2 seconds** (automatic mode): Check active displays → check physical connections → check lid state
3. **On status change**: Update `powercfg` → call `SetThreadExecutionState` → change icon → play sound
4. **Auto-sleep**: If lid closed + display off for 10 seconds → trigger sleep. Also: if external monitor disconnected in clamshell mode → sleep after 3 seconds.
5. **Exit**: Apply appropriate settings based on current monitor state → restore mouse wake if disabled → cleanup

### Configuration

- Settings file: `%AppData%\SleepMngr\settings.json`
- Log file: `%AppData%\SleepMngr\sleep_log.txt`
- Autostart registry key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SleepMngr`

## Build

```bash
# Compact EXE (~200 KB, requires .NET Runtime)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# Standalone EXE (~65 MB, includes .NET Runtime)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Debug build
dotnet build -c Debug
```

## Testing

Test project: [SleepMngr.Tests/](SleepMngr.Tests/)

```bash
dotnet test
```

Tests cover: `IconGenerator`, `PowerManager`, `Settings`, `WakeManager`, `WorkMode`.

## Code Conventions

- **Language**: C# 12 with nullable reference types enabled
- **Platform**: Windows 10/11 x64 only
- **UI**: Windows Forms (tray-only, no main window)
- **Naming**: PascalCase for public members, camelCase for private fields/methods
- **Error handling**: Silent `catch { }` for non-critical monitoring operations (display state checks, lid detection)
- **Logging**: Debug output via `System.Diagnostics.Debug.WriteLine` for development; user-facing log file for sleep failures

## Key Dependencies

- `System.Management` (NuGet) — WMI queries for monitor detection
- Windows Forms (`UseWindowsForms=true`) — tray icon, menus, message boxes
- Win32 P/Invoke — `EnumDisplayMonitors`, `SetThreadExecutionState`, `SetSuspendState`, `SendMessageW`
- `powercfg` CLI — lid action management, device wake settings
