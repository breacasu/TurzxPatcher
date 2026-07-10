# TurzxPatcher — A088 Display Recognition Patch for TURZX

> **Hinweis für Entwickler/lokale LLM:** Es gibt einen offenen Umsetzungsplan
> zur Erweiterung dieses Projekts um ein Plugin-System, siehe
> [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md). Das
> Schwesterprojekt für beliebige HWiNFO-Sensoren (inkl. Aquacomputer) liegt
> in `..\TurzxSensorBridge`.

Enables recognition of the **A088 WinUSB LCD display** (e.g., Lian Li 8.8" LCD) in the **TURZX** monitor software (V4.2.1.3).

## Problem

TURZX V4.2.1.3 does not recognize the A088 display (VID_1CBE&PID_A088) because:

1. The A088 device template has an **empty resolution string** (`res=""`). The Monitor's theme assignment logic (`Monitor.?()` case 3) requires a non-empty `res` that matches a `DevThemeType` entry. Only `320960` and `4801920` themes exist.
2. The device template has **incorrect width/height values** for portrait orientation — `width: 1920, height: 480` instead of `width: 480, height: 1920`.
3. The **IsPotrit flag is False**, which tells TURZX the display is landscape, causing 180° rotation and display issues.
4. TURZX reads its **base directory from `Environment.CommandLine`**, which breaks when the application is launched from a different location (e.g., a helper EXE in a temp folder).
5. Theme files (`.turtheme`) reference **specific assembly versions** of `UsbMonitorL` — if the version doesn't match, deserialization throws `FileNotFoundException`.
6. WPF's `Application.ResourceAssembly` defaults to `Assembly.GetEntryAssembly()`, which returns `null` or the wrong assembly when TURZX isn't the entry assembly, causing `ResourceDictionary.Source` errors.

## Solution

`TurzxPatcher.exe` is a **.NET Framework 4.8 x64** console application that:

1. **Patches A088 template `res`** to `4801920` at runtime via reflection.
2. **Fixes width/height values** — swaps from 1920x480 (landscape) to 480x1920 (portrait).
3. **Sets IsPotrit to True** — tells TURZX the display is mounted in portrait orientation.
4. **Fixes the base directory** by restarting itself from the TURZX directory as a new process, so `Environment.CommandLine` points to the correct location.
5. **Redirects assembly resolution** — any request for any version of `UsbMonitorL` returns the loaded assembly, fixing `.turtheme` deserialization.
6. **Fixes `GetEntryAssembly()`** by setting `AppDomainManager.m_entryAssembly` to the TURZX assembly, so WPF resource resolution works.
7. **Terminates L-Connect processes** before starting TURZX to prevent conflicts with USB communication.

## Requirements

- **Windows** (x64)
- **.NET Framework 4.8** (included with Windows 10/11)
- **TURZX V4.2.1.3** (`TURZX.exe`)
- A088 WinUSB display connected via USB
- **L-Connect closed** (the patcher will auto-terminate L-Connect processes)

## System Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| CPU | Any x64 processor | Modern multi-core |
| RAM | 4 GB | 8 GB+ |
| GPU | Any with x64 support | NVIDIA/AMD with latest drivers |
| Storage | 100 MB | 500 MB+ |
| USB | USB 2.0 | USB 3.0+ for display |

## Before You Start

1. **Close L-Connect completely** (system tray icon → Exit)
2. **Close any other display software** that might conflict
3. **Ensure TURZX.exe is in the correct directory**
4. **Run as Administrator** if you need HWiNFO sensor data access

## Important Notes

- **Keep TURZX running** — closing it causes the display to lose firmware state and go black
- **Do not disconnect the USB display** while TURZX is running
- **Run as Administrator** for full sensor data access (optional but recommended)
- **L-Connect must be closed** — the patcher will auto-terminate it, but manual closure is safer

## Usage

### Option 1: Place the patcher next to TURZX.exe

1. Copy `TurzxPatcher.exe` into the same folder as `TURZX.exe`.
2. Double-click `TurzxPatcher.exe` or run from terminal:
   ```
   TurzxPatcher.exe
   ```
3. The patcher detects the display, patches it, and launches TURZX's UI.

### Option 2: Specify the directory (run from anywhere)

```
TurzxPatcher.exe --dir "C:\Full\Path\To\TURZX-Folder"
```

### Option 3: Run as Administrator (for sensor data)

If TURZX needs access to HWiNFO sensor data, run PowerShell **as Administrator** first:

```
TurzxPatcher.exe --dir "C:\Full\Path\To\TURZX-Folder"
```

Or place the EXE in the TURZX folder and run as Administrator.

### Help

```
TurzxPatcher.exe --help
```

## Building from Source

### Prerequisites

- Visual Studio 2022 (or newer) with .NET desktop workload
- Or .NET SDK 4.8 targeting pack
- Git for version control

### Steps

```
git clone https://github.com/your-username/TurzxPatcher.git
cd TurzxPatcher
dotnet build -c Release
```

The output will be in `bin\Release\net48\TurzxPatcher.exe`.

### Project File

The project targets `net48` with `x64` platform and `UseWPF=true` (required for the WPF assembly resolver):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <UseWPF>true</UseWPF>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
</Project>
```

### Project Structure

```
TurzxPatcher/
├── src/                    # Source code
│   ├── Program.cs          # Main patcher logic
│   ├── TurzxPatcher.csproj # Project file
│   └── bin/                # Build output
├── bin/                    # Release builds
│   ├── TurzxPatcher.exe    # Ready-to-use patcher
│   └── TurzxPatcher.pdb    # Debug symbols
├── README.md               # This file
└── .gitignore              # Git ignore rules
```

## Technical Details

### Device Template Properties

The A088 device template contains these key properties:

| Property | Original Value | Patched Value | Purpose |
|----------|----------------|---------------|---------|
| `res` | `""` (empty) | `"4801920"` | Theme assignment in Monitor |
| `width` | `1920` | `480` | Display width for portrait |
| `height` | `480` | `1920` | Display height for portrait |
| `IsPotrit` | `False` | `True` | Portrait orientation flag |
| `dev_code` | `VID_1CBE&PID_A088` | Unchanged | Device identification |
| `name` | `8.8" Lian Li WinUSB` | Unchanged | Display name |

### Reflection Techniques

The patcher uses .NET reflection to:

1. **Load the TURZX assembly** without executing its entry point
2. **Access private fields** like `AppDomainManager.m_entryAssembly`
3. **Modify runtime properties** on device template objects
4. **Hook into assembly resolution** events
5. **Call internal methods** like `ScanMonitor` for device re-enumeration

### ConfuserEx Protection

TURZX.exe is protected by ConfuserEx with:

- **Instruction mutation**: Makes static analysis difficult
- **String encryption**: Encrypts string literals at runtime
- **Method obfuscation**: Makes method signatures harder to find

The patcher works around these protections by:

- Using reflection to find methods by metadata token
- Calling `걥()` which is NOT encrypted (only `군()` is encrypted)
- Loading the assembly and using runtime reflection for all operations

## How It Works

### Two-Process Architecture

The patcher uses two processes to ensure TURZX runs with the correct base directory:

**Launcher Process** (runs from wherever you placed TurzxPatcher.exe):
1. Terminates L-Connect processes to prevent USB conflicts
2. Copies itself (`TurzxPatcher.exe`) to the TURZX directory
3. Starts a Worker process from the TURZX directory
4. Exits

**Worker Process** (runs from TURZX directory):
1. Has correct `Environment.CommandLine` = path to copied patcher in TURZX dir
2. `Path.GetDirectoryName(CommandLine)` = TURZX base path
3. Loads TURZX.exe via reflection
4. Applies all runtime patches
5. Invokes TURZX entry point

### Why Two Processes?

TURZX's Monitor class derives all path fields (`AppDirPath`, `ThemePath`, `AppFontPath`) from `Environment.CommandLine`. If TurzxPatcher.exe runs from a different directory (e.g., Desktop), TURZX would look for its files in the wrong location. By copying itself to the TURZX directory and starting from there, the Worker process ensures all paths are correct.

### Runtime Patches Applied

| Patch | Technique | Purpose |
|-------|-----------|---------|
| A088 `res` → `4801920` | Reflection on `ScreenDevs` list | Theme matching in Monitor |
| Width/Height swap | Reflection on device template | Fix portrait orientation |
| IsPotrit → True | Reflection on device template | Tell TURZX display is portrait |
| Background rotation | Reflection on background properties | Fix 90° offset between layers |
| L-Connect termination | Process killing before startup | Prevent USB communication conflicts |
| `AssemblyResolve` | Event handler returning loaded assembly | Fix `.turtheme` deserialization |
| `GetEntryAssembly()` | `AppDomainManager.m_entryAssembly` | Fix WPF `ResourceDictionary.Source` |
| Base directory | Two-process with correct CWD | Fix all Monitor path fields |

### Runtime Flow

```
1. Launcher detects L-Connect processes
2. Launcher terminates L-Connect processes
3. Launcher copies itself to TURZX directory
4. Launcher starts Worker process from TURZX directory
5. Worker loads TURZX.exe via Assembly.LoadFrom
6. Worker sets up AssemblyResolve handler
7. Worker fixes GetEntryAssembly() via AppDomainManager
8. Worker patches A088 device template:
   - Sets res = "4801920"
   - Swaps width/height to 480x1920
   - Sets IsPotrit = True
   - Adjusts background rotation if needed
9. Worker invokes TURZX entry point
10. Watchdog thread monitors display state
```

## Debugging

The patcher includes comprehensive debug output. When you start TURZX with the patcher, you'll see:

```
Found A088 device template
Device template properties:
  DesktopMode: False
  RamStorage: False
  res:
  name: 8.8" Lian Li WinUSB
  width: 1920
  height: 480
  IsPotrit: False
  ...

Patched A088 -> res=4801920
Fixed dimensions: width=480, height=1920 (portrait)
Set IsPotrit to True (portrait mode)
```

This output helps identify what properties were patched and what values were corrected.

## Watchdog Feature

A background watchdog thread monitors the display state:

- **Monitors every 3 seconds** for display activity
- **Detects black screens** when `res` value is empty or `0000000`
- **Forces re-initialization** if display inactive for >30 seconds
- **Calls ScanMonitor** to attempt device re-enumeration

This helps recover from:
- TURZX being closed unexpectedly
- Display losing firmware state
- Theme switching failures

## Plugin-System (für Erweiterungen wie zusätzliche Sensordaten)

Ab Version 2.1.0 unterstützt TurzxPatcher ein einfaches Plugin-System, über das
externe Tools zusätzliche Laufzeit-Patches auf TURZX anwenden können, ohne
diesen Patcher selbst verändern zu müssen.

### Funktionsweise

1. Lege eine `.dll`-Datei, die das `TurzxShared.Plugins.ITurzxPatch`-Interface
   implementiert, in einen Unterordner `patches\` neben `TURZX.exe`.
2. Starte `TurzxPatcher.exe` wie gewohnt.
3. Nach dem A088-Display-Patch, aber vor dem Start von TURZX, werden alle
   gefundenen Plugins automatisch geladen und angewendet.

### Bekannte Plugins

- **TurzxSensorBridge** (separates Projekt): Ermöglicht die Nutzung beliebiger
  HWiNFO-Sensoren (inkl. Aquacomputer Wassertemperatur, Durchflussrate,
  Wasserqualität) als Datenquelle im TURZX Theme-Editor.
  Siehe: `..\TurzxSensorBridge`

### Eigene Plugins entwickeln

Das Interface `ITurzxPatch` (Version 1) ist in `src\Plugins\ITurzxPatch.cs`
dokumentiert. Ein Plugin ist eine .NET Framework 4.8 Class Library, die
mindestens eine öffentliche, nicht-abstrakte Klasse enthält, die dieses
Interface implementiert.

### Kollisionsschutz

TurzxPatcher belegt beim Start einen systemweiten Mutex (`Global\TurzxHostActive`),
um zu verhindern, dass mehrere Host-Prozesse (z.B. TurzxPatcher UND ein anderer
TURZX-Loader) gleichzeitig TURZX.exe laden. Ein zweiter Startversuch wird mit
einer klaren Fehlermeldung abgelehnt.

## Known Issues

- **180° rotation**: Fixed in v2.0.0 — width/height values and IsPotrit flag now correctly configure portrait orientation.
- **Background rotation**: Some themes may require background rotation adjustment. The patcher looks for background rotation properties and attempts to fix 90° offsets.
- **Display blackouts**: If the display goes completely black when TURZX closes, the watchdog attempts automatic re-initialization.
- **Administrator rights**: Some sensor data requires running as Administrator.
- **Windows 11 ARM64**: Not tested. Requires x64 emulation.

## Troubleshooting

### Display shows only bottom quarter of theme
- **Cause**: Width/height values are swapped (landscape instead of portrait)
- **Solution**: The patcher now automatically fixes this by swapping 1920x480 → 480x1920

### Display content is rotated 180°
- **Cause**: IsPotrit flag is False, telling TURZX the display is landscape
- **Solution**: The patcher now sets IsPotrit to True for portrait orientation

### Themes not loading or showing as black screens
- **Cause**: Empty resolution string in device template
- **Solution**: The patcher sets `res` to `4801920` which matches existing theme directories

### L-Connect conflicts
- **Symptoms**: Display not recognized, USB communication issues
- **Solution**: The patcher automatically terminates L-Connect processes before starting TURZX

### Display goes completely black after closing TURZX
- **Cause**: Firmware/ROM state lost on disconnect
- **Solution**: Keep TURZX running continuously, or use the watchdog for automatic re-initialization

### Theme editor preview shows correct orientation but display is wrong
- **Cause**: Runtime patch not applied to loaded assembly
- **Solution**: Restart TURZX with the new patcher

## Version History

### v2.1.0 (Current)
- Added plugin system (`ITurzxPatch` interface + `patches\` folder discovery)
- Added `Global\TurzxHostActive` mutex for multi-loader collision protection

### v2.0.0
- Fixed width/height values (1920x480 → 480x1920)
- Added IsPotrit flag setting (False → True)
- Added background rotation property detection
- Added L-Connect process termination
- Enhanced watchdog with display blackout recovery
- Comprehensive debugging output for device template properties

### v1.0.0
- Initial release with basic A088 recognition
- Fixed empty resolution string patch
- Two-process architecture for correct path resolution
- Assembly version redirect for .turtheme files

## License

MIT

## Credits

- **Lian Li** for the A088 display and TURZX software
- **TURZX community** for feedback and testing
- **Open Source contributors** for the .NET Framework and development tools

## Support

For issues, questions, or contributions:

1. **Check the Troubleshooting section** above
2. **Review the debug output** when starting TURZX
3. **Create an issue** on GitHub with:
   - TURZX version
   - Patcher version
   - Console output from the patcher
   - Screenshot of the issue

## Disclaimer

This tool is provided as-is for educational and experimental purposes. Use at your own risk. The developers are not responsible for any damage to your hardware or software. Always backup your TURZX.exe before using this patcher.

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Roadmap

- [ ] Support for additional display models
- [ ] GUI interface for easier configuration
- [ ] Automated theme testing
- [ ] Performance optimization
- [ ] Multi-language support
- [x] Plugin system for custom patches
