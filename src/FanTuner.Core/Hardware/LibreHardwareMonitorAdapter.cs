using System.Collections.Concurrent;
using FanTuner.Core.Models;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.Hardware;

/// <summary>
/// Hardware adapter implementation using LibreHardwareMonitor
/// </summary>
public sealed class LibreHardwareMonitorAdapter : IHardwareAdapter
{
    private readonly ILogger<LibreHardwareMonitorAdapter> _logger;
    private readonly Computer _computer;
    private readonly List<string> _warnings = new();
    private readonly ConcurrentDictionary<string, FanControlCapability> _fanCapabilities = new();
    private readonly ConcurrentDictionary<string, ISensor> _controlSensors = new();
    private readonly object _lock = new();
    private bool _isInitialized;
    private bool _isDisposed;

    public string AdapterName => "LibreHardwareMonitor";
    public bool IsAvailable => true;
    public bool IsInitialized => _isInitialized;
    public IReadOnlyList<string> Warnings => _warnings;

    public event EventHandler<HardwareChangedEventArgs>? HardwareChanged;

    public LibreHardwareMonitorAdapter(ILogger<LibreHardwareMonitorAdapter> logger)
    {
        _logger = logger;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false,
            IsControllerEnabled = true
        };
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return Task.CompletedTask;

        lock (_lock)
        {
            if (_isInitialized) return Task.CompletedTask;

            try
            {
                _logger.LogInformation("Initializing LibreHardwareMonitor...");
                _computer.Open();

                // Initial enumeration
                EnumerateHardware();

                _isInitialized = true;
                _logger.LogInformation("LibreHardwareMonitor initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize LibreHardwareMonitor");
                _warnings.Add($"Initialization error: {ex.Message}");
                throw;
            }
        }

        return Task.CompletedTask;
    }

    private void EnumerateHardware()
    {
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            _logger.LogDebug("Found hardware: {Name} ({Type})", hardware.Name, hardware.HardwareType);

            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Update();
                _logger.LogDebug("  Sub-hardware: {Name} ({Type})", subHardware.Name, subHardware.HardwareType);
            }

            // Check for fan control sensors
            var controlSensors = hardware.Sensors
                .Where(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Control)
                .ToList();

            foreach (var sensor in controlSensors)
            {
                var fanId = CreateFanId(hardware, sensor);
                _controlSensors[fanId.UniqueKey] = sensor;

                // Test if control is actually available
                var capability = TestFanControl(sensor);
                _fanCapabilities[fanId.UniqueKey] = capability;

                _logger.LogDebug("Fan control sensor: {Name} - {Capability}",
                    sensor.Name, capability);
            }
        }

        // Add warnings for common issues
        CheckForCommonIssues();
    }

    private FanControlCapability TestFanControl(ISensor sensor)
    {
        try
        {
            // Try to read control value
            if (sensor.Control == null)
                return FanControlCapability.MonitorOnly;

            // Check if software control is supported
            var control = sensor.Control;

            // Try a test write (set to current value)
            if (sensor.Value.HasValue)
            {
                control.SetSoftware(sensor.Value.Value);
                return FanControlCapability.FullControl;
            }

            return FanControlCapability.Unknown;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fan control test failed for {Name}", sensor.Name);
            return FanControlCapability.MonitorOnly;
        }
    }

    private void CheckForCommonIssues()
    {
        // Check for laptop
        var motherboard = _computer.Hardware
            .FirstOrDefault(h => h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Motherboard);

        if (motherboard != null)
        {
            var name = motherboard.Name.ToLowerInvariant();
            if (name.Contains("laptop") || name.Contains("notebook"))
            {
                _warnings.Add("Laptop detected - fan control may be limited by BIOS/EC");
            }
        }

        // Check for lack of controllable fans
        if (!_fanCapabilities.Values.Any(c => c == FanControlCapability.FullControl))
        {
            _warnings.Add("No controllable fans detected - running in monitor-only mode");
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        lock (_lock)
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                {
                    subHardware.Update();
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SensorReading>> GetSensorsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        var readings = new List<SensorReading>();

        lock (_lock)
        {
            foreach (var hardware in _computer.Hardware)
            {
                AddSensorsFromHardware(hardware, readings);
            }
        }

        return Task.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public Task<IReadOnlyList<SensorReading>> GetSensorsByTypeAsync(SensorType type, CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        var readings = new List<SensorReading>();
        var lhmType = MapToLhmSensorType(type);

        lock (_lock)
        {
            foreach (var hardware in _computer.Hardware)
            {
                foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == lhmType))
                {
                    readings.Add(CreateSensorReading(hardware, sensor));
                }

                foreach (var subHardware in hardware.SubHardware)
                {
                    foreach (var sensor in subHardware.Sensors.Where(s => s.SensorType == lhmType))
                    {
                        readings.Add(CreateSensorReading(subHardware, sensor));
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    public Task<SensorReading?> GetSensorAsync(SensorId id, CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        lock (_lock)
        {
            foreach (var hardware in _computer.Hardware)
            {
                var sensor = FindSensor(hardware, id);
                if (sensor != null)
                {
                    return Task.FromResult<SensorReading?>(CreateSensorReading(hardware, sensor));
                }

                foreach (var subHardware in hardware.SubHardware)
                {
                    sensor = FindSensor(subHardware, id);
                    if (sensor != null)
                    {
                        return Task.FromResult<SensorReading?>(CreateSensorReading(subHardware, sensor));
                    }
                }
            }
        }

        return Task.FromResult<SensorReading?>(null);
    }

    private ISensor? FindSensor(IHardware hardware, SensorId id)
    {
        return hardware.Sensors.FirstOrDefault(s =>
            hardware.Identifier.ToString() == id.HardwareId &&
            s.Name == id.SensorName &&
            MapToSensorType(s.SensorType) == id.Type);
    }

    public Task<IReadOnlyList<FanDevice>> GetFansAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        var fans = new List<FanDevice>();

        lock (_lock)
        {
            foreach (var hardware in _computer.Hardware)
            {
                AddFansFromHardware(hardware, fans);
            }
        }

        return Task.FromResult<IReadOnlyList<FanDevice>>(fans);
    }

    public Task<FanControlCapability> GetCapabilityAsync(FanId fanId, CancellationToken cancellationToken = default)
    {
        if (_fanCapabilities.TryGetValue(fanId.UniqueKey, out var capability))
        {
            return Task.FromResult(capability);
        }

        return Task.FromResult(FanControlCapability.Unknown);
    }

    public Task<bool> SetSpeedAsync(FanId fanId, float percent, CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        percent = Math.Clamp(percent, 0, 100);

        if (!_controlSensors.TryGetValue(fanId.UniqueKey, out var sensor))
        {
            _logger.LogWarning("Fan not found: {FanId}", fanId.UniqueKey);
            return Task.FromResult(false);
        }

        if (!_fanCapabilities.TryGetValue(fanId.UniqueKey, out var capability) ||
            capability != FanControlCapability.FullControl)
        {
            _logger.LogWarning("Fan control not available for: {FanId}", fanId.UniqueKey);
            return Task.FromResult(false);
        }

        try
        {
            lock (_lock)
            {
                sensor.Control?.SetSoftware(percent);
            }

            _logger.LogDebug("Set fan {FanId} to {Percent}%", fanId.UniqueKey, percent);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set fan speed for {FanId}", fanId.UniqueKey);
            _fanCapabilities[fanId.UniqueKey] = FanControlCapability.MonitorOnly;
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetAutoAsync(FanId fanId, CancellationToken cancellationToken = default)
    {
        ThrowIfNotInitialized();

        if (!_controlSensors.TryGetValue(fanId.UniqueKey, out var sensor))
        {
            return Task.FromResult(false);
        }

        try
        {
            lock (_lock)
            {
                sensor.Control?.SetDefault();
            }

            _logger.LogDebug("Set fan {FanId} to auto", fanId.UniqueKey);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set fan to auto for {FanId}", fanId.UniqueKey);
            return Task.FromResult(false);
        }
    }

    public async Task SetAllAutoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var fanIdKey in _controlSensors.Keys)
        {
            var parts = fanIdKey.Split(':');
            if (parts.Length >= 3)
            {
                var fanId = new FanId(parts[0], parts[1], int.TryParse(parts[2], out var idx) ? idx : 0);
                await SetAutoAsync(fanId, cancellationToken);
            }
        }
    }

    private void AddSensorsFromHardware(IHardware hardware, List<SensorReading> readings)
    {
        foreach (var sensor in hardware.Sensors)
        {
            readings.Add(CreateSensorReading(hardware, sensor));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            AddSensorsFromHardware(subHardware, readings);
        }
    }

    private void AddFansFromHardware(IHardware hardware, List<FanDevice> fans)
    {
        // Get fan RPM sensors
        var fanSensors = hardware.Sensors
            .Where(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan)
            .ToList();

        // Get control sensors
        var controlSensors = hardware.Sensors
            .Where(s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Control)
            .ToList();

        for (int i = 0; i < fanSensors.Count; i++)
        {
            var fanSensor = fanSensors[i];
            var fanId = CreateFanId(hardware, fanSensor, i);

            // Try to find matching control sensor
            var controlSensor = controlSensors.FirstOrDefault(c =>
                c.Name.Replace("Control", "").Trim() == fanSensor.Name.Replace("Fan", "").Trim());

            var capability = FanControlCapability.MonitorOnly;
            float? currentPercent = null;

            if (controlSensor != null)
            {
                var controlId = CreateFanId(hardware, controlSensor, i);
                if (_fanCapabilities.TryGetValue(controlId.UniqueKey, out capability))
                {
                    currentPercent = controlSensor.Value;
                }
            }

            fans.Add(new FanDevice
            {
                Id = fanId,
                DisplayName = fanSensor.Name,
                HardwareName = hardware.Name,
                Capability = capability,
                CurrentRpm = fanSensor.Value ?? 0,
                CurrentPercent = currentPercent,
                LastUpdate = DateTime.UtcNow
            });
        }

        // Also add control-only fans (no RPM sensor)
        foreach (var controlSensor in controlSensors)
        {
            var matchingFan = fanSensors.Any(f =>
                f.Name.Replace("Fan", "").Trim() == controlSensor.Name.Replace("Control", "").Trim());

            if (!matchingFan)
            {
                var fanId = CreateFanId(hardware, controlSensor);
                _fanCapabilities.TryGetValue(fanId.UniqueKey, out var capability);

                fans.Add(new FanDevice
                {
                    Id = fanId,
                    DisplayName = controlSensor.Name,
                    HardwareName = hardware.Name,
                    Capability = capability,
                    CurrentRpm = 0,
                    CurrentPercent = controlSensor.Value,
                    LastUpdate = DateTime.UtcNow
                });
            }
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            AddFansFromHardware(subHardware, fans);
        }
    }

    private SensorReading CreateSensorReading(IHardware hardware, ISensor sensor)
    {
        return new SensorReading
        {
            Id = new SensorId(
                hardware.Identifier.ToString(),
                sensor.Name,
                MapToSensorType(sensor.SensorType)
            ),
            DisplayName = sensor.Name,
            HardwareName = hardware.Name,
            HardwareType = MapToHardwareType(hardware.HardwareType),
            Value = sensor.Value ?? 0,
            Min = sensor.Min,
            Max = sensor.Max,
            Unit = GetUnit(sensor.SensorType),
            Timestamp = DateTime.UtcNow
        };
    }

    private static FanId CreateFanId(IHardware hardware, ISensor sensor, int index = 0)
    {
        return new FanId(hardware.Identifier.ToString(), sensor.Name, index);
    }

    private static Models.SensorType MapToSensorType(LibreHardwareMonitor.Hardware.SensorType type)
    {
        return type switch
        {
            LibreHardwareMonitor.Hardware.SensorType.Voltage => Models.SensorType.Voltage,
            LibreHardwareMonitor.Hardware.SensorType.Current => Models.SensorType.Current,
            LibreHardwareMonitor.Hardware.SensorType.Clock => Models.SensorType.Clock,
            LibreHardwareMonitor.Hardware.SensorType.Temperature => Models.SensorType.Temperature,
            LibreHardwareMonitor.Hardware.SensorType.Load => Models.SensorType.Load,
            LibreHardwareMonitor.Hardware.SensorType.Frequency => Models.SensorType.Frequency,
            LibreHardwareMonitor.Hardware.SensorType.Fan => Models.SensorType.Fan,
            LibreHardwareMonitor.Hardware.SensorType.Flow => Models.SensorType.Flow,
            LibreHardwareMonitor.Hardware.SensorType.Control => Models.SensorType.Control,
            LibreHardwareMonitor.Hardware.SensorType.Level => Models.SensorType.Level,
            LibreHardwareMonitor.Hardware.SensorType.Factor => Models.SensorType.Factor,
            LibreHardwareMonitor.Hardware.SensorType.Power => Models.SensorType.Power,
            LibreHardwareMonitor.Hardware.SensorType.Data => Models.SensorType.Data,
            LibreHardwareMonitor.Hardware.SensorType.SmallData => Models.SensorType.SmallData,
            LibreHardwareMonitor.Hardware.SensorType.Throughput => Models.SensorType.Throughput,
            LibreHardwareMonitor.Hardware.SensorType.Energy => Models.SensorType.Energy,
            LibreHardwareMonitor.Hardware.SensorType.Noise => Models.SensorType.Noise,
            LibreHardwareMonitor.Hardware.SensorType.Humidity => Models.SensorType.Humidity,
            _ => Models.SensorType.Data
        };
    }

    private static LibreHardwareMonitor.Hardware.SensorType MapToLhmSensorType(Models.SensorType type)
    {
        return type switch
        {
            Models.SensorType.Temperature => LibreHardwareMonitor.Hardware.SensorType.Temperature,
            Models.SensorType.Fan => LibreHardwareMonitor.Hardware.SensorType.Fan,
            Models.SensorType.Load => LibreHardwareMonitor.Hardware.SensorType.Load,
            Models.SensorType.Voltage => LibreHardwareMonitor.Hardware.SensorType.Voltage,
            Models.SensorType.Clock => LibreHardwareMonitor.Hardware.SensorType.Clock,
            Models.SensorType.Power => LibreHardwareMonitor.Hardware.SensorType.Power,
            Models.SensorType.Control => LibreHardwareMonitor.Hardware.SensorType.Control,
            _ => LibreHardwareMonitor.Hardware.SensorType.Data
        };
    }

    private static Models.HardwareType MapToHardwareType(LibreHardwareMonitor.Hardware.HardwareType type)
    {
        return type switch
        {
            LibreHardwareMonitor.Hardware.HardwareType.Motherboard => Models.HardwareType.Motherboard,
            LibreHardwareMonitor.Hardware.HardwareType.SuperIO => Models.HardwareType.SuperIO,
            LibreHardwareMonitor.Hardware.HardwareType.Cpu => Models.HardwareType.Cpu,
            LibreHardwareMonitor.Hardware.HardwareType.Memory => Models.HardwareType.Memory,
            LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia => Models.HardwareType.GpuNvidia,
            LibreHardwareMonitor.Hardware.HardwareType.GpuAmd => Models.HardwareType.GpuAmd,
            LibreHardwareMonitor.Hardware.HardwareType.GpuIntel => Models.HardwareType.GpuIntel,
            LibreHardwareMonitor.Hardware.HardwareType.Storage => Models.HardwareType.Storage,
            LibreHardwareMonitor.Hardware.HardwareType.Network => Models.HardwareType.Network,
            LibreHardwareMonitor.Hardware.HardwareType.Cooler => Models.HardwareType.Cooler,
            LibreHardwareMonitor.Hardware.HardwareType.EmbeddedController => Models.HardwareType.EmbeddedController,
            LibreHardwareMonitor.Hardware.HardwareType.Psu => Models.HardwareType.Psu,
            LibreHardwareMonitor.Hardware.HardwareType.Battery => Models.HardwareType.Battery,
            _ => Models.HardwareType.Unknown
        };
    }

    private static string GetUnit(LibreHardwareMonitor.Hardware.SensorType type)
    {
        return type switch
        {
            LibreHardwareMonitor.Hardware.SensorType.Temperature => "°C",
            LibreHardwareMonitor.Hardware.SensorType.Fan => "RPM",
            LibreHardwareMonitor.Hardware.SensorType.Load => "%",
            LibreHardwareMonitor.Hardware.SensorType.Voltage => "V",
            LibreHardwareMonitor.Hardware.SensorType.Clock => "MHz",
            LibreHardwareMonitor.Hardware.SensorType.Power => "W",
            LibreHardwareMonitor.Hardware.SensorType.Control => "%",
            _ => ""
        };
    }

    private void ThrowIfNotInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Hardware monitor not initialized. Call InitializeAsync first.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                // Return all fans to auto before closing
                foreach (var sensor in _controlSensors.Values)
                {
                    try
                    {
                        sensor.Control?.SetDefault();
                    }
                    catch { }
                }

                _computer.Close();
                _logger.LogInformation("LibreHardwareMonitor disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing LibreHardwareMonitor");
            }
        }
    }
}
