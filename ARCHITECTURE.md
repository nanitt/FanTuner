# PC Fan Tuner - Architecture Document

## Solution Structure

```
FanTuner/
├── FanTuner.sln
├── src/
│   ├── FanTuner.Core/              # Shared library
│   │   ├── Hardware/               # Hardware abstractions
│   │   │   ├── IHardwareMonitor.cs
│   │   │   ├── IFanController.cs
│   │   │   ├── ISensor.cs
│   │   │   ├── IFanDevice.cs
│   │   │   ├── LibreHardwareMonitorAdapter.cs
│   │   │   └── MockHardwareAdapter.cs
│   │   ├── Models/                 # Domain models
│   │   │   ├── SensorReading.cs
│   │   │   ├── FanCurve.cs
│   │   │   ├── FanProfile.cs
│   │   │   ├── CurvePoint.cs
│   │   │   └── AppConfiguration.cs
│   │   ├── Services/               # Business logic
│   │   │   ├── CurveEngine.cs
│   │   │   ├── SafetyManager.cs
│   │   │   ├── ConfigurationManager.cs
│   │   │   └── ProfileManager.cs
│   │   ├── IPC/                    # Inter-process communication
│   │   │   ├── PipeMessages.cs
│   │   │   ├── PipeServer.cs
│   │   │   └── PipeClient.cs
│   │   └── Logging/
│   │       └── FileLogger.cs
│   │
│   ├── FanTuner.Service/           # Windows Service
│   │   ├── Program.cs
│   │   ├── FanTunerService.cs
│   │   ├── ServiceWorker.cs
│   │   └── appsettings.json
│   │
│   └── FanTuner.UI/                # WPF Application
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── FansViewModel.cs
│       │   ├── ProfilesViewModel.cs
│       │   └── SettingsViewModel.cs
│       ├── Views/
│       │   ├── DashboardView.xaml
│       │   ├── FansView.xaml
│       │   ├── ProfilesView.xaml
│       │   └── SettingsView.xaml
│       ├── Controls/
│       │   ├── CurveEditor.xaml
│       │   ├── SensorGauge.xaml
│       │   └── FanCard.xaml
│       ├── Converters/
│       │   └── ValueConverters.cs
│       ├── Themes/
│       │   ├── Dark.xaml
│       │   └── Light.xaml
│       └── Resources/
│           └── Styles.xaml
│
├── tests/
│   └── FanTuner.Tests/
│       ├── CurveEngineTests.cs
│       ├── SafetyManagerTests.cs
│       └── ConfigurationTests.cs
│
├── installer/
│   ├── FanTuner.Installer.wixproj
│   └── Product.wxs
│
└── docs/
    ├── FEASIBILITY.md
    ├── ARCHITECTURE.md
    └── BUILD.md
```

## Data Models

### Core Entities

```csharp
// Sensor identification and reading
public record SensorId(string HardwareId, string SensorName, SensorType Type);

public enum SensorType
{
    Temperature,
    Fan,
    Load,
    Voltage,
    Clock
}

public record SensorReading(
    SensorId Id,
    string DisplayName,
    float Value,
    float? Min,
    float? Max,
    string Unit,
    DateTime Timestamp,
    bool IsStale = false
);

// Fan device and control
public record FanId(string HardwareId, string FanName, int Index);

public enum FanControlMode
{
    Auto,       // BIOS/hardware control
    Manual,     // Fixed percentage
    Curve       // Temperature-based curve
}

public enum FanControlCapability
{
    FullControl,    // Can read and set speed
    MonitorOnly,    // Can only read RPM
    Unknown         // Not yet determined
}

public record FanDevice(
    FanId Id,
    string DisplayName,
    FanControlCapability Capability,
    float CurrentRpm,
    float? CurrentPercent,
    float? MinPercent,
    float? MaxPercent
);

// Fan curve definition
public record CurvePoint(float Temperature, float FanPercent);

public class FanCurve
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default";
    public SensorId SourceSensor { get; set; }  // Which temp to follow
    public List<CurvePoint> Points { get; set; } = new();
    public float MinPercent { get; set; } = 20f;
    public float MaxPercent { get; set; } = 100f;
    public float Hysteresis { get; set; } = 2f;  // Degrees before changing
}

// Profile (collection of fan assignments)
public class FanProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default";
    public bool IsDefault { get; set; }
    public Dictionary<string, FanAssignment> FanAssignments { get; set; } = new();
}

public class FanAssignment
{
    public FanId FanId { get; set; }
    public FanControlMode Mode { get; set; }
    public float? ManualPercent { get; set; }     // For Manual mode
    public string? CurveId { get; set; }          // For Curve mode
}

// Application configuration
public class AppConfiguration
{
    public string Version { get; set; } = "1.0";
    public int PollIntervalMs { get; set; } = 1000;
    public float EmergencyCpuTemp { get; set; } = 95f;
    public float EmergencyGpuTemp { get; set; } = 90f;
    public float EmergencyHysteresis { get; set; } = 5f;
    public float DefaultMinFanPercent { get; set; } = 20f;
    public string ActiveProfileId { get; set; }
    public bool StartMinimized { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public List<FanCurve> Curves { get; set; } = new();
    public List<FanProfile> Profiles { get; set; } = new();
}
```

## IPC Message Contracts

### Named Pipe Protocol

- Pipe name: `\\.\pipe\FanTuner`
- Format: Length-prefixed JSON messages
- Max message size: 1MB

```csharp
// Base message
public abstract class PipeMessage
{
    public string MessageType { get; set; }
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Request messages (UI → Service)
public class GetStatusRequest : PipeMessage { }
public class GetSensorsRequest : PipeMessage { }
public class GetFansRequest : PipeMessage { }
public class GetConfigRequest : PipeMessage { }
public class SetConfigRequest : PipeMessage
{
    public AppConfiguration Config { get; set; }
}
public class SetFanSpeedRequest : PipeMessage
{
    public string FanId { get; set; }
    public float Percent { get; set; }
}
public class SetProfileRequest : PipeMessage
{
    public string ProfileId { get; set; }
}
public class SubscribeSensorsRequest : PipeMessage { }
public class UnsubscribeSensorsRequest : PipeMessage { }

// Response messages (Service → UI)
public class StatusResponse : PipeMessage
{
    public bool IsRunning { get; set; }
    public string Version { get; set; }
    public int UptimeSeconds { get; set; }
    public bool EmergencyModeActive { get; set; }
    public string ActiveProfileName { get; set; }
    public List<string> Warnings { get; set; }
}

public class SensorsResponse : PipeMessage
{
    public List<SensorReading> Sensors { get; set; }
}

public class FansResponse : PipeMessage
{
    public List<FanDevice> Fans { get; set; }
}

public class ConfigResponse : PipeMessage
{
    public AppConfiguration Config { get; set; }
}

public class ErrorResponse : PipeMessage
{
    public string Error { get; set; }
    public string Details { get; set; }
}

// Push notification (Service → UI, subscription)
public class SensorUpdateNotification : PipeMessage
{
    public List<SensorReading> Sensors { get; set; }
    public List<FanDevice> Fans { get; set; }
}
```

## Hardware Abstraction Layer

```csharp
public interface IHardwareMonitor : IDisposable
{
    Task InitializeAsync();
    Task<IReadOnlyList<SensorReading>> GetSensorsAsync();
    Task RefreshAsync();
    event EventHandler<HardwareChangedEventArgs> HardwareChanged;
}

public interface IFanController : IDisposable
{
    Task<IReadOnlyList<FanDevice>> GetFansAsync();
    Task<FanControlCapability> GetCapabilityAsync(FanId fanId);
    Task<bool> SetSpeedAsync(FanId fanId, float percent);
    Task<bool> SetAutoAsync(FanId fanId);  // Return to BIOS control
}

public interface IHardwareAdapter : IHardwareMonitor, IFanController
{
    string AdapterName { get; }
    bool IsAvailable { get; }
}
```

## Curve Interpolation Algorithm

```csharp
public static class CurveEngine
{
    /// <summary>
    /// Calculate fan percentage for given temperature using curve points.
    /// Uses smooth cosine interpolation between points.
    /// </summary>
    public static float Interpolate(FanCurve curve, float temperature, float? lastOutput = null)
    {
        if (curve.Points.Count == 0)
            return curve.MinPercent;

        if (curve.Points.Count == 1)
            return Math.Clamp(curve.Points[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        var sorted = curve.Points.OrderBy(p => p.Temperature).ToList();

        // Below first point
        if (temperature <= sorted[0].Temperature)
            return Math.Clamp(sorted[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        // Above last point
        if (temperature >= sorted[^1].Temperature)
            return Math.Clamp(sorted[^1].FanPercent, curve.MinPercent, curve.MaxPercent);

        // Find surrounding points
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (temperature >= sorted[i].Temperature && temperature <= sorted[i + 1].Temperature)
            {
                float t = (temperature - sorted[i].Temperature) /
                         (sorted[i + 1].Temperature - sorted[i].Temperature);

                // Cosine interpolation for smooth transitions
                float smoothT = (1 - MathF.Cos(t * MathF.PI)) / 2;

                float result = sorted[i].FanPercent +
                              (sorted[i + 1].FanPercent - sorted[i].FanPercent) * smoothT;

                // Apply hysteresis if we have previous output
                if (lastOutput.HasValue)
                {
                    result = ApplyHysteresis(result, lastOutput.Value, curve.Hysteresis);
                }

                return Math.Clamp(result, curve.MinPercent, curve.MaxPercent);
            }
        }

        return curve.MinPercent;
    }

    private static float ApplyHysteresis(float target, float current, float hysteresis)
    {
        // Only change if difference exceeds hysteresis threshold
        if (MathF.Abs(target - current) < hysteresis)
            return current;
        return target;
    }
}
```

## Service Lifecycle

```
┌─────────────────────────────────────────────────────────────────┐
│                     SERVICE STARTUP                             │
├─────────────────────────────────────────────────────────────────┤
│ 1. Load configuration from %ProgramData%\FanTuner\config.json   │
│ 2. Initialize LibreHardwareMonitor                              │
│ 3. Enumerate sensors and fans                                   │
│ 4. Validate stored curves reference valid sensors               │
│ 5. Start named pipe server                                      │
│ 6. Apply active profile's fan settings                          │
│ 7. Begin sensor polling loop                                    │
│ 8. Register for power events                                    │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     MAIN LOOP (every poll interval)             │
├─────────────────────────────────────────────────────────────────┤
│ 1. Read all sensors                                             │
│ 2. Check safety conditions                                      │
│    - If emergency → set all fans to 100%                        │
│    - If sensor failure → mark stale, continue with cached       │
│ 3. For each fan in active profile:                              │
│    - If Auto → do nothing (BIOS controls)                       │
│    - If Manual → set to fixed percentage                        │
│    - If Curve → calculate from curve, apply with hysteresis     │
│ 4. Push sensor updates to subscribed UI clients                 │
│ 5. Log readings (if configured)                                 │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                     SERVICE SHUTDOWN                            │
├─────────────────────────────────────────────────────────────────┤
│ 1. Save current configuration                                   │
│ 2. Set all controlled fans to Auto (return to BIOS)             │
│ 3. Disconnect all pipe clients                                  │
│ 4. Dispose hardware monitor                                     │
│ 5. Flush logs                                                   │
└─────────────────────────────────────────────────────────────────┘
```

## Error Handling Strategy

| Error Type | Handling |
|------------|----------|
| Sensor read failure | Mark as stale, use cached value, log warning |
| Fan control failure | Mark as monitor-only, log error, continue |
| Config file corrupt | Use defaults, backup corrupt file, log error |
| Pipe client disconnect | Clean up, continue serving others |
| Hardware disappears | Remove from list, update UI, log info |
| Exception in main loop | Log, increment failure counter, continue |
| 5 consecutive failures | Enter emergency mode, alert user |
