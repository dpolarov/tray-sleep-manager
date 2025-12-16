# SleepMngr

A Windows program that prevents the laptop from going to sleep when the lid is closed if an external monitor is connected.

## Features

- 🖥️ Automatic detection of connected monitors (via WMI)
- 💤 Prevents sleep when lid is closed
- ⏰ **Automatic sleep after 10 seconds** - in automatic mode, if the lid is already closed and the display turns off, the laptop automatically goes to sleep after 10 seconds
- 🎛️ Three operating modes:
  - **Automatic** - determined by the presence of an external monitor
  - **Always stay awake** - lid closure does not affect operation
  - **Always sleep** - laptop sleeps when lid is closed
- 🔄 Automatically changes Windows settings on every monitor connection/disconnection
- 📊 Colored system tray icon:
  - 🔵 **Blue** - automatic mode, sleep allowed
  - 🟡 **Yellow** - automatic mode, sleep prevented
  - 🔷 **Dark Blue** - manual mode "🔷 Always sleep"
  - 🟠 **Orange** - manual mode "🟠 Always stay awake"
- 🔔 Sound notifications when protection status changes
- 🔒 Single instance application (new copy replaces the old one)
- 🔧 Manual restoration of lid settings
- ℹ️ Detailed status window with information about all displays

## Requirements

### To run the ready-made EXE:
- Windows 10/11 (x64)
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop Runtime)

### To build from source:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### 🚀 Quick build via CMD scripts

Simply run the desired `.cmd` file with a double click:

- **`build-compact.cmd`** - compact EXE (~200 KB, requires .NET Runtime) ⭐ recommended
- **`build-standalone.cmd`** - standalone EXE (~65 MB, does not require .NET Runtime)
- **`build-debug.cmd`** - regular build with DLLs (for development)
- **`clean.cmd`** - clean all build files

---

### 📋 Manual build via PowerShell

Open PowerShell in the project folder.

### Option 1: Single EXE with external .NET Runtime (recommended)

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

**Result:** `bin\Release\net8.0-windows\win-x64\publish\SleepMngr.exe`

**Characteristics:**
- ✅ Size: **~200 KB**
- ✅ Single EXE file (bundled: SleepMngr.dll + System.Management.dll)
- ⚠️ Requires: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the target system

**When to use:** For personal use or distribution in an organization where .NET Runtime is already installed.

---

### Option 2: Standalone EXE with embedded .NET Runtime

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Result:** `bin\Release\net8.0-windows\win-x64\publish\SleepMngr.exe`

**Characteristics:**
- ✅ Size: **~65 MB**
- ✅ Single EXE file (includes the entire .NET Runtime)
- ✅ Does not require .NET Runtime installation
- ✅ Works on any Windows 10/11 x64 "out of the box"

**When to use:** For distribution to end-users who may not have the .NET Runtime.

---

### Option 3: Regular build with DLL files

```powershell
dotnet build -c Release
```

**Result:** `bin\Release\net8.0-windows\win-x64\`

**Characteristics:**
- 📁 Folder with multiple files (EXE + DLL)
- ⚡ Fast build for development and testing
- ⚠️ Requires: .NET 8.0 Runtime

**When to use:** For development and debugging.

## Launch

1. Run `SleepMngr.exe`
2. The program will appear in the system tray (bottom right corner)
3. When an external monitor is connected, the icon will change to a shield
4. Double-clicking the icon will show the current status

## Usage

### Tray Menu

- **Status: [mode] • [state]** - shows current mode and state, clicking opens detailed monitor information
- **Operating Mode** ▶
  - 🔄 **Automatic** (default) - determined by external monitor presence
  -  **Always stay awake** - laptop does not sleep when lid is closed
  -  **Always sleep** - laptop sleeps when lid is closed
- **Restore lid settings** - forcibly returns settings to "Sleep"
- **💤 Sleep now** - immediately puts the computer to sleep without any checks
- **📋 Open log** - opens the log file with information about sleep attempts
- **Exit** - closes the program and applies settings based on the current status

### Autostart with Windows

1. Press `Win + R`
2. Type `shell:startup` and press Enter
3. Create a shortcut to `SleepMngr.exe` in the opened folder

## How it works

### Operating Modes:

**🔄 Automatic (default):**
- ✅ External monitor **connected** → laptop **does NOT sleep** when lid is closed
- ❌ No external monitor → laptop **sleeps** when lid is closed
- ⏰ **Smart Sleep**: if the lid is already closed and the display turns off, the laptop automatically goes to sleep after 10 seconds
- When switching to this mode, settings are forcibly applied based on the current status

**🟡 Always stay awake:**
- Laptop never sleeps when lid is closed
- Regardless of external monitors

**🔵 Always sleep:**
- Laptop always sleeps when lid is closed
- Regardless of external monitors

### Technical Process:

1. **On Launch**: 
   - Checks for single instance (closes old copy if present)
   - Detects external monitors
   - Applies appropriate sleep settings

2. **Every 2 seconds** (in automatic mode):
   - Checks active displays: `Screen.AllScreens`
   - Checks physically connected: WMI (`WmiMonitorID`)
   - Monitors lid and display state for automatic sleep

3. **On Status Change**:
   - Changes Windows settings via `powercfg`: lid close action
   - Calls `SetThreadExecutionState` to prevent sleep
   - Changes icon depending on mode:
     - 🔵 Blue - auto + sleep
     - 🟡 Yellow - auto + stay awake
     - 🔷 Dark Blue - manual "Always sleep"
     - 🟠 Orange - manual "Always stay awake"
   - Plays a sound notification

4. **On Exit**:
   - Checks current monitor status
   - Applies appropriate settings based on external monitor presence

### Automatic Sleep (New Feature):

In **Automatic mode**, the program monitors a specific situation:
- Laptop lid is **already closed** (determined by absence of built-in display in active list)
- Display **turned off** (e.g., due to power saving timeout or manually)

When both conditions are met, the program:
1. ⏱️ Starts a 10-second timer
2. 💤 After 10 seconds, automatically sends the laptop to sleep
3. ✅ Timer resets if display turns on earlier

**Usage Examples:**
- **Scenario 1**: Working with an external monitor, lid closed. You walk away, display turns off after 5 minutes → laptop goes to sleep 10 seconds after display turns off
- **Scenario 2**: Working only with laptop, lid open. Display turns off → laptop does NOT go to sleep (lid open)
- **Scenario 3**: Closed the lid, but display is still active → laptop does NOT go to sleep immediately, waits for display to turn off + 10 seconds

**Note**: This feature works ONLY in automatic mode. In manual modes ("Always stay awake" / "Always sleep"), automatic sleep is disabled.

## Technical Details

- **MonitorDetector** - detects connected monitors via `EnumDisplayMonitors` API and WMI
- **PowerManager** - manages power state via `SetThreadExecutionState` API and several sleep methods
- **TrayApplicationContext** - manages tray icon and application logic

### Sleep Methods:

The program **automatically detects the sleep mode type** of your laptop:

#### Modern Standby (S0 Low Power Idle)
Modern laptops use **Modern Standby** instead of classic S3:
- ✅ Program **turns off display** instead of calling SetSuspendState
- ✅ System automatically enters S0 Low Power Idle
- ✅ Works **instantly** without delays
- ✅ Compatible with Connected Standby

#### Classic Mode (S3 Sleep)
For older laptops with S3 support, the program tests **10 methods**:
1. SetSuspendState(forceCritical=true, disableWakeEvent=false)
2. SetSuspendState(forceCritical=false, disableWakeEvent=false)
3. SetSuspendState(forceCritical=true, disableWakeEvent=true)
4. Application.SetSuspendState(force=true)
5. Application.SetSuspendState(force=false)
6. rundll32.exe powrprof.dll,SetSuspendState Suspend
7. rundll32.exe powrprof.dll,SetSuspendState 0,1,0
8. cmd.exe /c rundll32.exe
9. PowerShell with Add-Type
10. PowerShell with P/Invoke

**The program automatically chooses the correct method** depending on your system.

## Notes

- The program does not change system power settings permanently
- The effect only lasts while the program is running
- On program exit, everything returns to standard settings
- Requires standard user rights (administrator not needed)

## Troubleshooting

**Program does not start:**
- Ensure .NET 8.0 Runtime is installed
- Check antivirus - add program to exceptions

**Laptop still sleeps:**
- Ensure external monitor is detected by the system (Settings → System → Display)
- Check status by double-clicking the tray icon

**"Sleep now" does not work:**
- The program tries 5 different sleep methods in sequence
- On error, a dialog will appear offering to open the log file
- Log file: `%AppData%\SleepMngr\sleep_log.txt`
- The log indicates which methods were tried and why they failed
- Usually Method 1 (`Application.SetSuspendState`) works without admin rights
- If none work:
  1. Check Windows power settings
  2. Close applications blocking sleep
  3. Try running as administrator
  4. Check Windows group policies (gpedit.msc)

**Automatic sleep after 10 seconds does not work:**
- Ensure automatic mode is enabled
- Verify lid is actually closed (status should show "Physically connected: 2+")
- Display must turn off (by timeout or manually)

**I want to change the check interval:**
- Open `TrayApplicationContext.cs`
- Change `monitorCheckTimer.Interval` value (in milliseconds)

## Project Structure

```
SleepMngr/
├── build-compact.cmd          # Compact EXE build
├── build-standalone.cmd       # Standalone EXE build
├── build-debug.cmd           # Regular build for development
├── clean.cmd                 # Clean build files
├── SleepMngr.csproj    # Project configuration
├── Program.cs                # Entry point, single instance
├── TrayApplicationContext.cs # Main logic, tray UI
├── MonitorDetector.cs        # Monitor detection
├── PowerManager.cs           # Sleep management
├── LidActionManager.cs       # Lid settings management
├── IconGenerator.cs          # Colored icon generation
├── WorkMode.cs              # Operating modes Enum
└── README.md                # Documentation
```
