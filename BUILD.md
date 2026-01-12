# FanTuner - Build & Run Instructions

## Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Visual Studio 2022** (recommended) or VS Code with C# extension
- **Administrator privileges** (for service installation)

## Quick Start

### 1. Clone and Build

```powershell
# Clone the repository
cd C:\Projects
git clone https://github.com/yourorg/FanTuner.git
cd FanTuner

# Restore packages and build
dotnet restore
dotnet build -c Release
```

### 2. Run Tests

```powershell
dotnet test --logger "console;verbosity=detailed"
```

### 3. Run in Development Mode (Mock Hardware)

```powershell
# Start the service with mock hardware (no admin required for testing)
dotnet run --project src/FanTuner.Service -- --mock

# In another terminal, start the UI
dotnet run --project src/FanTuner.UI
```

### 4. Run with Real Hardware

**Warning:** Running with real hardware requires administrator privileges and can control actual fan speeds.

```powershell
# Build release
dotnet build -c Release

# Run service as admin (PowerShell as Administrator)
.\src\FanTuner.Service\bin\Release\net8.0-windows\FanTuner.Service.exe
```

## Service Installation

### Install as Windows Service

```powershell
# Run PowerShell as Administrator

# Build release version
dotnet publish src/FanTuner.Service -c Release -o C:\Program Files\FanTuner\Service

# Install the service
sc.exe create "FanTuner" binPath="C:\Program Files\FanTuner\Service\FanTuner.Service.exe" start=auto
sc.exe description "FanTuner" "PC Fan Control Service - Monitors temperatures and controls fan speeds"

# Start the service
sc.exe start FanTuner
```

### Uninstall Service

```powershell
# Run PowerShell as Administrator
sc.exe stop FanTuner
sc.exe delete FanTuner
```

### Check Service Status

```powershell
sc.exe query FanTuner
```

## UI Installation

```powershell
# Build and copy UI
dotnet publish src/FanTuner.UI -c Release -o "C:\Program Files\FanTuner\UI"

# Create shortcut (optional)
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\FanTuner.lnk")
$Shortcut.TargetPath = "C:\Program Files\FanTuner\UI\FanTuner.exe"
$Shortcut.Save()
```

## Project Structure

```
FanTuner/
├── src/
│   ├── FanTuner.Core/        # Shared library (models, services, IPC)
│   ├── FanTuner.Service/     # Windows Service (background fan control)
│   └── FanTuner.UI/          # WPF Application (user interface)
├── tests/
│   └── FanTuner.Tests/       # Unit tests
├── installer/                 # WiX installer project
└── docs/                      # Documentation
```

## Configuration

Configuration is stored in:
- **Service config:** `%ProgramData%\FanTuner\config.json`
- **Logs:** `%ProgramData%\FanTuner\logs\`
- **UI logs:** `%LocalAppData%\FanTuner\logs\`

### Example config.json

```json
{
  "version": "1.0",
  "pollIntervalMs": 1000,
  "emergencyCpuTemp": 95,
  "emergencyGpuTemp": 90,
  "emergencyHysteresis": 5,
  "defaultMinFanPercent": 20,
  "activeProfileId": "default-profile-id",
  "startMinimized": false,
  "minimizeToTray": true,
  "theme": "Dark",
  "enableFileLogging": true,
  "logLevel": "Info",
  "curves": [...],
  "profiles": [...]
}
```

## Troubleshooting

### Service won't start

1. **Check Windows Event Viewer** for errors:
   - Event Viewer > Windows Logs > Application
   - Look for "FanTuner" source

2. **Check service logs:**
   ```powershell
   Get-Content "C:\ProgramData\FanTuner\logs\fantuner-service.log" -Tail 50
   ```

3. **Verify .NET 8 is installed:**
   ```powershell
   dotnet --list-runtimes
   ```

### UI can't connect to service

1. Ensure service is running:
   ```powershell
   sc.exe query FanTuner
   ```

2. Check named pipe permissions (service should allow user connections)

3. Check firewall isn't blocking (named pipes shouldn't need firewall rules)

### No fans detected

1. **Run as Administrator** - hardware access requires admin privileges

2. **Check LibreHardwareMonitor compatibility:**
   - Some newer motherboards may not be fully supported
   - Check [LibreHardwareMonitor issues](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues)

3. **Laptop users:** Most laptops lock fan control at the EC level. FanTuner will show "Monitor Only" for these systems.

### Fan control not working

1. Check if fan shows "Controllable" or "Monitor Only" in the UI
2. Some motherboards require specific SuperIO chips for control
3. Try closing other fan control software (SpeedFan, HWiNFO, etc.)

### Build errors

1. **Restore packages:**
   ```powershell
   dotnet restore --force
   ```

2. **Clean and rebuild:**
   ```powershell
   dotnet clean
   dotnet build -c Release
   ```

3. **Check .NET SDK version:**
   ```powershell
   dotnet --version  # Should be 8.0.x
   ```

## Development

### Running with debugger

1. Open `FanTuner.sln` in Visual Studio 2022
2. Set startup project to `FanTuner.Service` or `FanTuner.UI`
3. Press F5 to debug

### Adding a new fan curve algorithm

1. Add method to `CurveEngine.cs`
2. Add unit tests to `CurveEngineTests.cs`
3. Update UI if needed

### Adding new sensor types

1. Update `SensorTypes.cs` enum
2. Update `LibreHardwareMonitorAdapter.cs` mapping
3. Update UI display formatters

## Release Build

```powershell
# Full release build
dotnet publish src/FanTuner.Service -c Release -r win-x64 --self-contained false
dotnet publish src/FanTuner.UI -c Release -r win-x64 --self-contained false

# Or self-contained (includes .NET runtime)
dotnet publish src/FanTuner.Service -c Release -r win-x64 --self-contained true
dotnet publish src/FanTuner.UI -c Release -r win-x64 --self-contained true
```

## Known Limitations

1. **Laptop fan control** - Most laptops don't expose fan control to software
2. **USB fan controllers** - Corsair iCUE, NZXT CAM devices not supported (v1.0)
3. **AIO coolers** - Limited support depending on USB HID implementation
4. **NVIDIA GPU fans** - May require unsigned driver or older NVAPI

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Submit a pull request

## License

MIT License - See LICENSE file
