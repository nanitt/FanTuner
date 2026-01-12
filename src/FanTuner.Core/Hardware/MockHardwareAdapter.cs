using System.Collections.Concurrent;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.Hardware;

/// <summary>
/// Mock hardware adapter for testing without real hardware
/// </summary>
public sealed class MockHardwareAdapter : IHardwareAdapter
{
    private readonly ILogger<MockHardwareAdapter> _logger;
    private readonly Random _random = new();
    private readonly ConcurrentDictionary<string, float> _fanSpeeds = new();
    private readonly ConcurrentDictionary<string, FanControlMode> _fanModes = new();
    private bool _isInitialized;
    private float _baseCpuTemp = 45f;
    private float _baseGpuTemp = 40f;

    public string AdapterName => "Mock";
    public bool IsAvailable => true;
    public bool IsInitialized => _isInitialized;
    public IReadOnlyList<string> Warnings => new[] { "Running in mock mode - no real hardware" };

    public event EventHandler<HardwareChangedEventArgs>? HardwareChanged;

    // Mock hardware IDs
    private const string CpuId = "/mock/cpu/0";
    private const string GpuId = "/mock/gpu/0";
    private const string MoboId = "/mock/motherboard/0";

    public MockHardwareAdapter(ILogger<MockHardwareAdapter> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing mock hardware adapter");

        // Initialize mock fans with default values
        _fanSpeeds[$"{MoboId}:CPU Fan:0"] = 50f;
        _fanSpeeds[$"{MoboId}:System Fan 1:1"] = 40f;
        _fanSpeeds[$"{MoboId}:System Fan 2:2"] = 40f;
        _fanSpeeds[$"{GpuId}:GPU Fan:0"] = 30f;

        foreach (var key in _fanSpeeds.Keys)
        {
            _fanModes[key] = FanControlMode.Auto;
        }

        _isInitialized = true;
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Simulate temperature fluctuation
        _baseCpuTemp += (float)(_random.NextDouble() - 0.5) * 2;
        _baseCpuTemp = Math.Clamp(_baseCpuTemp, 30f, 95f);

        _baseGpuTemp += (float)(_random.NextDouble() - 0.5) * 2;
        _baseGpuTemp = Math.Clamp(_baseGpuTemp, 25f, 90f);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SensorReading>> GetSensorsAsync(CancellationToken cancellationToken = default)
    {
        var sensors = new List<SensorReading>
        {
            // CPU sensors
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Package", SensorType.Temperature, _baseCpuTemp),
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Core #1", SensorType.Temperature, _baseCpuTemp + _random.Next(-5, 5)),
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Core #2", SensorType.Temperature, _baseCpuTemp + _random.Next(-5, 5)),
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Total", SensorType.Load, 20f + (float)_random.NextDouble() * 60),
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Core #1", SensorType.Clock, 3600f + _random.Next(-200, 800)),
            CreateSensor(CpuId, "Mock CPU", HardwareType.Cpu, "CPU Package", SensorType.Power, 15f + (float)_random.NextDouble() * 50),

            // GPU sensors
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Core", SensorType.Temperature, _baseGpuTemp),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Hot Spot", SensorType.Temperature, _baseGpuTemp + 10),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Core", SensorType.Load, 10f + (float)_random.NextDouble() * 80),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Core", SensorType.Clock, 1800f + _random.Next(-100, 400)),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Memory", SensorType.Temperature, _baseGpuTemp - 5),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Power", SensorType.Power, 30f + (float)_random.NextDouble() * 150),

            // Motherboard sensors
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "System", SensorType.Temperature, 35f + (float)_random.NextDouble() * 10),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "Chipset", SensorType.Temperature, 45f + (float)_random.NextDouble() * 15),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "VRM", SensorType.Temperature, 40f + (float)_random.NextDouble() * 20),

            // Fan RPM sensors
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "CPU Fan", SensorType.Fan, GetFanRpm("CPU Fan", 0)),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "System Fan 1", SensorType.Fan, GetFanRpm("System Fan 1", 1)),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "System Fan 2", SensorType.Fan, GetFanRpm("System Fan 2", 2)),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Fan", SensorType.Fan, GetFanRpm("GPU Fan", 0, GpuId)),

            // Fan control sensors
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "CPU Fan Control", SensorType.Control, _fanSpeeds[$"{MoboId}:CPU Fan:0"]),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "System Fan 1 Control", SensorType.Control, _fanSpeeds[$"{MoboId}:System Fan 1:1"]),
            CreateSensor(MoboId, "Mock Motherboard", HardwareType.Motherboard, "System Fan 2 Control", SensorType.Control, _fanSpeeds[$"{MoboId}:System Fan 2:2"]),
            CreateSensor(GpuId, "Mock GPU", HardwareType.GpuNvidia, "GPU Fan Control", SensorType.Control, _fanSpeeds[$"{GpuId}:GPU Fan:0"])
        };

        return Task.FromResult<IReadOnlyList<SensorReading>>(sensors);
    }

    public async Task<IReadOnlyList<SensorReading>> GetSensorsByTypeAsync(SensorType type, CancellationToken cancellationToken = default)
    {
        var all = await GetSensorsAsync(cancellationToken);
        return all.Where(s => s.Id.Type == type).ToList();
    }

    public async Task<SensorReading?> GetSensorAsync(SensorId id, CancellationToken cancellationToken = default)
    {
        var all = await GetSensorsAsync(cancellationToken);
        return all.FirstOrDefault(s => s.Id.UniqueKey == id.UniqueKey);
    }

    public Task<IReadOnlyList<FanDevice>> GetFansAsync(CancellationToken cancellationToken = default)
    {
        var fans = new List<FanDevice>
        {
            CreateFan(MoboId, "CPU Fan", 0, "Mock Motherboard", FanControlCapability.FullControl),
            CreateFan(MoboId, "System Fan 1", 1, "Mock Motherboard", FanControlCapability.FullControl),
            CreateFan(MoboId, "System Fan 2", 2, "Mock Motherboard", FanControlCapability.FullControl),
            CreateFan(GpuId, "GPU Fan", 0, "Mock GPU", FanControlCapability.FullControl)
        };

        return Task.FromResult<IReadOnlyList<FanDevice>>(fans);
    }

    public Task<FanControlCapability> GetCapabilityAsync(FanId fanId, CancellationToken cancellationToken = default)
    {
        // All mock fans are controllable
        return Task.FromResult(FanControlCapability.FullControl);
    }

    public Task<bool> SetSpeedAsync(FanId fanId, float percent, CancellationToken cancellationToken = default)
    {
        percent = Math.Clamp(percent, 0, 100);
        _fanSpeeds[fanId.UniqueKey] = percent;
        _fanModes[fanId.UniqueKey] = FanControlMode.Manual;

        _logger.LogDebug("Mock: Set fan {FanId} to {Percent}%", fanId.UniqueKey, percent);
        return Task.FromResult(true);
    }

    public Task<bool> SetAutoAsync(FanId fanId, CancellationToken cancellationToken = default)
    {
        _fanModes[fanId.UniqueKey] = FanControlMode.Auto;
        _logger.LogDebug("Mock: Set fan {FanId} to auto", fanId.UniqueKey);
        return Task.FromResult(true);
    }

    public Task SetAllAutoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in _fanModes.Keys)
        {
            _fanModes[key] = FanControlMode.Auto;
        }
        return Task.CompletedTask;
    }

    private SensorReading CreateSensor(string hardwareId, string hardwareName, HardwareType hwType,
        string sensorName, SensorType sensorType, float value)
    {
        return new SensorReading
        {
            Id = new SensorId(hardwareId, sensorName, sensorType),
            DisplayName = sensorName,
            HardwareName = hardwareName,
            HardwareType = hwType,
            Value = value,
            Timestamp = DateTime.UtcNow
        };
    }

    private FanDevice CreateFan(string hardwareId, string fanName, int index, string hardwareName,
        FanControlCapability capability)
    {
        var fanId = new FanId(hardwareId, fanName, index);
        _fanSpeeds.TryGetValue(fanId.UniqueKey, out var percent);

        return new FanDevice
        {
            Id = fanId,
            DisplayName = fanName,
            HardwareName = hardwareName,
            Capability = capability,
            CurrentRpm = GetFanRpm(fanName, index, hardwareId),
            CurrentPercent = percent,
            MinPercent = 0,
            MaxPercent = 100,
            LastUpdate = DateTime.UtcNow
        };
    }

    private float GetFanRpm(string fanName, int index, string hardwareId = MoboId)
    {
        var key = $"{hardwareId}:{fanName}:{index}";
        if (_fanSpeeds.TryGetValue(key, out var percent))
        {
            // Simulate RPM based on percentage (max ~2000 RPM)
            return percent * 20 + _random.Next(-50, 50);
        }
        return 800 + _random.Next(-50, 50);
    }

    /// <summary>
    /// Simulate load to change temperatures (for testing)
    /// </summary>
    public void SimulateLoad(float cpuLoad, float gpuLoad)
    {
        _baseCpuTemp = 35f + cpuLoad * 0.6f;
        _baseGpuTemp = 30f + gpuLoad * 0.65f;
    }

    public void Dispose()
    {
        _logger.LogInformation("Mock hardware adapter disposed");
    }
}
