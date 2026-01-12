using FanTuner.Core.Hardware;
using FanTuner.Core.Models;
using FanTuner.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FanTuner.Tests;

public class SafetyManagerTests
{
    private readonly Mock<IFanController> _fanControllerMock;
    private readonly SafetyManager _safetyManager;

    public SafetyManagerTests()
    {
        _fanControllerMock = new Mock<IFanController>();
        _fanControllerMock.Setup(x => x.GetFansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FanDevice>());

        _safetyManager = new SafetyManager(
            NullLogger<SafetyManager>.Instance,
            _fanControllerMock.Object);
    }

    [Fact]
    public void GetStatus_Initially_NotInEmergencyMode()
    {
        var status = _safetyManager.GetStatus();

        status.IsEmergencyMode.Should().BeFalse();
        status.EmergencyReason.Should().BeNull();
    }

    [Fact]
    public void UpdateThresholds_UpdatesValues()
    {
        var config = new AppConfiguration
        {
            EmergencyCpuTemp = 100f,
            EmergencyGpuTemp = 95f,
            EmergencyHysteresis = 10f,
            DefaultMinFanPercent = 30f
        };

        _safetyManager.UpdateThresholds(config);

        // Verify by testing enforcement
        var result = _safetyManager.EnforceMinimum(15f);
        result.Should().Be(30f);
    }

    [Fact]
    public async Task CheckSensorsAsync_NormalTemps_NotEmergency()
    {
        var sensors = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 60f),
            CreateTempSensor("GPU", HardwareType.GpuNvidia, 55f)
        };

        var isEmergency = await _safetyManager.CheckSensorsAsync(sensors);

        isEmergency.Should().BeFalse();
        _safetyManager.GetStatus().IsEmergencyMode.Should().BeFalse();
    }

    [Fact]
    public async Task CheckSensorsAsync_CpuOverThreshold_EntersEmergency()
    {
        var config = AppConfiguration.CreateDefault();
        config.EmergencyCpuTemp = 90f;
        _safetyManager.UpdateThresholds(config);

        var sensors = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 95f), // Over 90
            CreateTempSensor("GPU", HardwareType.GpuNvidia, 55f)
        };

        var isEmergency = await _safetyManager.CheckSensorsAsync(sensors);

        isEmergency.Should().BeTrue();
        _safetyManager.GetStatus().IsEmergencyMode.Should().BeTrue();
        _safetyManager.GetStatus().EmergencyReason.Should().Contain("CPU");
    }

    [Fact]
    public async Task CheckSensorsAsync_GpuOverThreshold_EntersEmergency()
    {
        var config = AppConfiguration.CreateDefault();
        config.EmergencyGpuTemp = 85f;
        _safetyManager.UpdateThresholds(config);

        var sensors = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 60f),
            CreateTempSensor("GPU", HardwareType.GpuNvidia, 90f) // Over 85
        };

        var isEmergency = await _safetyManager.CheckSensorsAsync(sensors);

        isEmergency.Should().BeTrue();
        _safetyManager.GetStatus().EmergencyReason.Should().Contain("GPU");
    }

    [Fact]
    public async Task CheckSensorsAsync_EmergencyMode_ExitsWithHysteresis()
    {
        var config = AppConfiguration.CreateDefault();
        config.EmergencyCpuTemp = 90f;
        config.EmergencyHysteresis = 5f;
        _safetyManager.UpdateThresholds(config);

        // Enter emergency
        var sensors1 = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 95f)
        };
        await _safetyManager.CheckSensorsAsync(sensors1);
        _safetyManager.GetStatus().IsEmergencyMode.Should().BeTrue();

        // Temp drops but still above threshold - hysteresis
        var sensors2 = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 87f) // Below 90 but above 85 (90-5)
        };
        await _safetyManager.CheckSensorsAsync(sensors2);
        _safetyManager.GetStatus().IsEmergencyMode.Should().BeTrue(); // Still in emergency

        // Temp drops below hysteresis threshold
        var sensors3 = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 80f) // Below 85
        };
        await _safetyManager.CheckSensorsAsync(sensors3);
        _safetyManager.GetStatus().IsEmergencyMode.Should().BeFalse(); // Exited
    }

    [Fact]
    public void EnforceMinimum_BelowMinimum_ReturnsMinimum()
    {
        var config = AppConfiguration.CreateDefault();
        config.DefaultMinFanPercent = 25f;
        _safetyManager.UpdateThresholds(config);

        var result = _safetyManager.EnforceMinimum(10f);

        result.Should().Be(25f);
    }

    [Fact]
    public void EnforceMinimum_AboveMinimum_ReturnsOriginal()
    {
        var config = AppConfiguration.CreateDefault();
        config.DefaultMinFanPercent = 25f;
        _safetyManager.UpdateThresholds(config);

        var result = _safetyManager.EnforceMinimum(50f);

        result.Should().Be(50f);
    }

    [Fact]
    public void ValidateFanSpeed_ValidSpeed_ReturnsSafe()
    {
        var fan = new FanDevice { CurrentRpm = 1000 };

        var (isSafe, warning) = _safetyManager.ValidateFanSpeed(50f, fan);

        isSafe.Should().BeTrue();
        warning.Should().BeNull();
    }

    [Fact]
    public void ValidateFanSpeed_OutOfRange_ReturnsUnsafe()
    {
        var fan = new FanDevice { CurrentRpm = 1000 };

        var (isSafe, warning) = _safetyManager.ValidateFanSpeed(150f, fan);

        isSafe.Should().BeFalse();
        warning.Should().Contain("out of range");
    }

    [Fact]
    public void ValidateFanSpeed_BelowMinimum_ReturnsUnsafe()
    {
        var config = AppConfiguration.CreateDefault();
        config.DefaultMinFanPercent = 20f;
        _safetyManager.UpdateThresholds(config);

        var fan = new FanDevice { CurrentRpm = 1000 };

        var (isSafe, warning) = _safetyManager.ValidateFanSpeed(10f, fan);

        isSafe.Should().BeFalse();
        warning.Should().Contain("below minimum");
    }

    [Fact]
    public void ValidateFanSpeed_ZeroPercentRunningFan_ReturnsWarning()
    {
        var fan = new FanDevice { CurrentRpm = 1200 };

        var (isSafe, warning) = _safetyManager.ValidateFanSpeed(0f, fan);

        // It's technically safe but has a warning
        isSafe.Should().BeTrue();
        warning.Should().Contain("stop completely");
    }

    [Fact]
    public async Task RecordFailureAsync_MultipleFailures_EntersEmergency()
    {
        // Record multiple failures
        for (int i = 0; i < 5; i++)
        {
            await _safetyManager.RecordFailureAsync("Test failure");
        }

        _safetyManager.GetStatus().IsEmergencyMode.Should().BeTrue();
        _safetyManager.GetStatus().EmergencyReason.Should().Contain("consecutive failures");
    }

    [Fact]
    public async Task CheckSensorsAsync_ResetsFailureCounter()
    {
        // Record some failures
        await _safetyManager.RecordFailureAsync("Test failure");
        await _safetyManager.RecordFailureAsync("Test failure");

        _safetyManager.GetStatus().ConsecutiveFailures.Should().Be(2);

        // Successful sensor check
        var sensors = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 50f)
        };
        await _safetyManager.CheckSensorsAsync(sensors);

        _safetyManager.GetStatus().ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void SafetyAlert_FiredOnEmergencyEntry()
    {
        var alertRaised = false;
        _safetyManager.SafetyAlert += (s, e) => alertRaised = true;

        var config = AppConfiguration.CreateDefault();
        config.EmergencyCpuTemp = 90f;
        _safetyManager.UpdateThresholds(config);

        var sensors = new List<SensorReading>
        {
            CreateTempSensor("CPU", HardwareType.Cpu, 95f)
        };
        _safetyManager.CheckSensorsAsync(sensors).Wait();

        alertRaised.Should().BeTrue();
    }

    private static SensorReading CreateTempSensor(string name, HardwareType hwType, float value)
    {
        return new SensorReading
        {
            Id = new SensorId($"/test/{name.ToLower()}", name, SensorType.Temperature),
            DisplayName = name,
            HardwareName = $"Test {hwType}",
            HardwareType = hwType,
            Value = value,
            Timestamp = DateTime.UtcNow
        };
    }
}
