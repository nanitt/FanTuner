# FanTuner - PC Fan Control for Windows

A modern Windows desktop application for monitoring system temperatures and controlling fan speeds.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)
![Windows](https://img.shields.io/badge/Platform-Windows-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Real-time Monitoring**: CPU, GPU, motherboard temperatures and fan RPMs
- **Fan Control**: Create custom fan curves or set manual speeds
- **Multiple Profiles**: Quick, Balanced, Performance - switch with one click
- **Background Service**: Fans stay controlled even when UI is closed
- **Safety Features**: Emergency mode if temperatures exceed thresholds
- **Modern UI**: Dark/Light theme, clean WPF interface

## Screenshots

*(Add screenshots here)*

## Quick Start

### Prerequisites

- Windows 10/11 (64-bit)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Administrator privileges (for hardware access)

### Installation

1. Download the latest release
2. Run the installer
3. Start FanTuner from the Start Menu

### Building from Source

```powershell
git clone https://github.com/yourorg/FanTuner.git
cd FanTuner
dotnet build -c Release
```

See [BUILD.md](BUILD.md) for detailed build instructions.

## Architecture

```
┌─────────────┐     Named Pipe     ┌─────────────────┐
│  FanTuner   │◄──────────────────►│  FanTuner       │
│  UI (WPF)   │                    │  Service        │
└─────────────┘                    └────────┬────────┘
                                            │
                                            ▼
                                   ┌─────────────────┐
                                   │ LibreHardware   │
                                   │ Monitor         │
                                   └────────┬────────┘
                                            │
                                            ▼
                                   ┌─────────────────┐
                                   │  Hardware       │
                                   │  (Sensors/Fans) │
                                   └─────────────────┘
```

## Hardware Compatibility

### Fully Supported
- Intel & AMD CPUs (temperature monitoring)
- NVIDIA GPUs (temperature, some fan control)
- AMD GPUs (temperature, some fan control)
- Most motherboard sensors

### Limited Support
- Motherboard fan control (depends on SuperIO chip)
- AIO liquid coolers (some models)

### Not Supported (v1.0)
- USB fan controllers (Corsair, NZXT)
- Most laptop fan control
- Proprietary RGB/fan combo devices

See [FEASIBILITY.md](FEASIBILITY.md) for detailed compatibility information.

## Configuration

Configuration is stored in `%ProgramData%\FanTuner\config.json`

### Fan Curve Example

```json
{
  "name": "Balanced",
  "points": [
    { "temperature": 30, "fanPercent": 20 },
    { "temperature": 50, "fanPercent": 40 },
    { "temperature": 70, "fanPercent": 80 },
    { "temperature": 85, "fanPercent": 100 }
  ],
  "minPercent": 20,
  "maxPercent": 100,
  "hysteresis": 2
}
```

## Safety

FanTuner includes multiple safety features:

1. **Emergency Mode**: If CPU > 95°C or GPU > 90°C, all fans go to 100%
2. **Minimum Fan Speed**: Configurable minimum to prevent fan stall
3. **Watchdog**: Service failures revert fans to BIOS control
4. **Graceful Degradation**: Stale sensors use cached values

## Command Line

### Service

```powershell
# Run with mock hardware (testing)
FanTuner.Service.exe --mock

# Check service status
sc.exe query FanTuner
```

### Logs

```powershell
# View service logs
Get-Content "C:\ProgramData\FanTuner\logs\fantuner-service.log" -Tail 50

# View UI logs
Get-Content "$env:LOCALAPPDATA\FanTuner\logs\fantuner-ui.log" -Tail 50
```

## Development

### Project Structure

- `FanTuner.Core` - Shared models, services, IPC
- `FanTuner.Service` - Windows service for fan control
- `FanTuner.UI` - WPF application
- `FanTuner.Tests` - Unit tests

### Running Tests

```powershell
dotnet test
```

### Tech Stack

- .NET 8
- WPF with MVVM (CommunityToolkit.Mvvm)
- LibreHardwareMonitor for hardware access
- Named pipes for IPC
- xUnit + FluentAssertions for testing

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) file

## Acknowledgments

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Hardware monitoring library
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM framework

## Support

- [Issue Tracker](https://github.com/yourorg/FanTuner/issues)
- [Discussions](https://github.com/yourorg/FanTuner/discussions)
