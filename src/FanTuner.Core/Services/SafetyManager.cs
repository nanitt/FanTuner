using FanTuner.Core.Hardware;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.Services;

/// <summary>
/// Event args for safety alerts
/// </summary>
public class SafetyAlertEventArgs : EventArgs
{
    public string Message { get; }
    public SafetyAlertLevel Level { get; }
    public DateTime Timestamp { get; }

    public SafetyAlertEventArgs(string message, SafetyAlertLevel level)
    {
        Message = message;
        Level = level;
        Timestamp = DateTime.UtcNow;
    }
}

public enum SafetyAlertLevel
{
    Info,
    Warning,
    Critical,
    Emergency
}

/// <summary>
/// Current safety status
/// </summary>
public class SafetyStatus
{
    public bool IsEmergencyMode { get; set; }
    public string? EmergencyReason { get; set; }
    public DateTime? EmergencyStartTime { get; set; }
    public float? TriggeringTemperature { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool IsDegraded { get; set; }
    public List<string> ActiveWarnings { get; set; } = new();
}

/// <summary>
/// Manages safety rules and emergency fan control
/// </summary>
public class SafetyManager
{
    private readonly ILogger<SafetyManager> _logger;
    private readonly IFanController _fanController;
    private readonly object _lock = new();

    private float _emergencyCpuTemp = 95f;
    private float _emergencyGpuTemp = 90f;
    private float _emergencyHysteresis = 5f;
    private float _minimumFanPercent = 20f;
    private int _maxConsecutiveFailures = 5;

    private bool _isEmergencyMode;
    private string? _emergencyReason;
    private DateTime? _emergencyStartTime;
    private float? _triggeringTemp;
    private int _consecutiveFailures;
    private readonly List<string> _activeWarnings = new();

    public event EventHandler<SafetyAlertEventArgs>? SafetyAlert;

    public SafetyManager(ILogger<SafetyManager> logger, IFanController fanController)
    {
        _logger = logger;
        _fanController = fanController;
    }

    /// <summary>
    /// Update safety thresholds from configuration
    /// </summary>
    public void UpdateThresholds(AppConfiguration config)
    {
        lock (_lock)
        {
            _emergencyCpuTemp = config.EmergencyCpuTemp;
            _emergencyGpuTemp = config.EmergencyGpuTemp;
            _emergencyHysteresis = config.EmergencyHysteresis;
            _minimumFanPercent = config.DefaultMinFanPercent;
        }

        _logger.LogInformation(
            "Safety thresholds updated: CPU={CpuTemp}°C, GPU={GpuTemp}°C, Hysteresis={Hysteresis}°C, MinFan={MinFan}%",
            _emergencyCpuTemp, _emergencyGpuTemp, _emergencyHysteresis, _minimumFanPercent);
    }

    /// <summary>
    /// Get current safety status
    /// </summary>
    public SafetyStatus GetStatus()
    {
        lock (_lock)
        {
            return new SafetyStatus
            {
                IsEmergencyMode = _isEmergencyMode,
                EmergencyReason = _emergencyReason,
                EmergencyStartTime = _emergencyStartTime,
                TriggeringTemperature = _triggeringTemp,
                ConsecutiveFailures = _consecutiveFailures,
                IsDegraded = _consecutiveFailures > 0,
                ActiveWarnings = new List<string>(_activeWarnings)
            };
        }
    }

    /// <summary>
    /// Check sensor readings for safety conditions
    /// </summary>
    /// <returns>True if in emergency mode (all fans should be 100%)</returns>
    public async Task<bool> CheckSensorsAsync(IReadOnlyList<SensorReading> sensors, CancellationToken cancellationToken = default)
    {
        var tempSensors = sensors.Where(s => s.Id.Type == SensorType.Temperature).ToList();

        // Check CPU temperatures
        var cpuTemps = tempSensors
            .Where(s => s.HardwareType == HardwareType.Cpu)
            .ToList();

        var maxCpuTemp = cpuTemps.Any() ? cpuTemps.Max(s => s.Value) : 0f;

        // Check GPU temperatures
        var gpuTemps = tempSensors
            .Where(s => s.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToList();

        var maxGpuTemp = gpuTemps.Any() ? gpuTemps.Max(s => s.Value) : 0f;

        // Check for stale sensors
        var staleSensors = sensors.Where(s => s.IsStale).ToList();

        bool shouldEnterEmergency = false;
        string? emergencyReason = null;
        float? triggeringTemp = null;

        // Check CPU emergency
        if (maxCpuTemp >= _emergencyCpuTemp)
        {
            shouldEnterEmergency = true;
            emergencyReason = $"CPU temperature critical: {maxCpuTemp:F1}°C";
            triggeringTemp = maxCpuTemp;
        }

        // Check GPU emergency
        if (maxGpuTemp >= _emergencyGpuTemp)
        {
            shouldEnterEmergency = true;
            emergencyReason = $"GPU temperature critical: {maxGpuTemp:F1}°C";
            triggeringTemp = maxGpuTemp;
        }

        // Handle emergency mode transitions
        if (shouldEnterEmergency && !_isEmergencyMode)
        {
            await EnterEmergencyModeAsync(emergencyReason!, triggeringTemp!.Value, cancellationToken);
        }
        else if (_isEmergencyMode && !shouldEnterEmergency)
        {
            // Check if we can exit emergency mode (with hysteresis)
            bool canExit = true;

            if (maxCpuTemp > (_emergencyCpuTemp - _emergencyHysteresis))
                canExit = false;

            if (maxGpuTemp > (_emergencyGpuTemp - _emergencyHysteresis))
                canExit = false;

            if (canExit)
            {
                ExitEmergencyMode();
            }
        }

        // Update warnings
        UpdateWarnings(cpuTemps, gpuTemps, staleSensors);

        // Reset failure counter on successful read
        lock (_lock)
        {
            _consecutiveFailures = 0;
        }

        return _isEmergencyMode;
    }

    /// <summary>
    /// Record a sensor read failure
    /// </summary>
    public async Task RecordFailureAsync(string reason, CancellationToken cancellationToken = default)
    {
        int failures;
        lock (_lock)
        {
            _consecutiveFailures++;
            failures = _consecutiveFailures;
        }

        _logger.LogWarning("Sensor read failure ({Count}/{Max}): {Reason}",
            failures, _maxConsecutiveFailures, reason);

        if (failures >= _maxConsecutiveFailures)
        {
            await EnterEmergencyModeAsync(
                $"Too many consecutive failures ({failures})",
                0,
                cancellationToken);
        }
    }

    /// <summary>
    /// Enforce minimum fan speed
    /// </summary>
    public float EnforceMinimum(float targetPercent)
    {
        return Math.Max(targetPercent, _minimumFanPercent);
    }

    /// <summary>
    /// Check if fan speed is safe
    /// </summary>
    public (bool IsSafe, string? Warning) ValidateFanSpeed(float percent, FanDevice fan)
    {
        if (percent < 0 || percent > 100)
            return (false, $"Fan speed {percent}% is out of range (0-100)");

        if (percent < _minimumFanPercent)
            return (false, $"Fan speed {percent}% is below minimum ({_minimumFanPercent}%)");

        if (percent == 0 && fan.CurrentRpm > 0)
            return (true, "Setting fan to 0% may cause it to stop completely");

        return (true, null);
    }

    private async Task EnterEmergencyModeAsync(string reason, float triggeringTemp, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_isEmergencyMode) return;

            _isEmergencyMode = true;
            _emergencyReason = reason;
            _emergencyStartTime = DateTime.UtcNow;
            _triggeringTemp = triggeringTemp;
        }

        _logger.LogCritical("EMERGENCY MODE ACTIVATED: {Reason}", reason);

        // Set all fans to 100%
        try
        {
            var fans = await _fanController.GetFansAsync(cancellationToken);
            foreach (var fan in fans.Where(f => f.CanControl))
            {
                await _fanController.SetSpeedAsync(fan.Id, 100f, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set emergency fan speeds");
        }

        RaiseSafetyAlert(reason, SafetyAlertLevel.Emergency);
    }

    private void ExitEmergencyMode()
    {
        lock (_lock)
        {
            if (!_isEmergencyMode) return;

            _isEmergencyMode = false;
            var duration = DateTime.UtcNow - _emergencyStartTime;

            _logger.LogInformation(
                "Emergency mode deactivated after {Duration}. Reason was: {Reason}",
                duration, _emergencyReason);

            _emergencyReason = null;
            _emergencyStartTime = null;
            _triggeringTemp = null;
        }

        RaiseSafetyAlert("Emergency mode deactivated - temperatures normal", SafetyAlertLevel.Info);
    }

    private void UpdateWarnings(
        List<SensorReading> cpuTemps,
        List<SensorReading> gpuTemps,
        List<SensorReading> staleSensors)
    {
        lock (_lock)
        {
            _activeWarnings.Clear();

            // High temperature warnings (but below emergency)
            var maxCpu = cpuTemps.Any() ? cpuTemps.Max(s => s.Value) : 0f;
            var maxGpu = gpuTemps.Any() ? gpuTemps.Max(s => s.Value) : 0f;

            if (maxCpu >= _emergencyCpuTemp - 10 && maxCpu < _emergencyCpuTemp)
                _activeWarnings.Add($"CPU temperature high: {maxCpu:F1}°C");

            if (maxGpu >= _emergencyGpuTemp - 10 && maxGpu < _emergencyGpuTemp)
                _activeWarnings.Add($"GPU temperature high: {maxGpu:F1}°C");

            // Stale sensor warnings
            foreach (var stale in staleSensors)
            {
                _activeWarnings.Add($"Sensor data stale: {stale.DisplayName}");
            }
        }
    }

    private void RaiseSafetyAlert(string message, SafetyAlertLevel level)
    {
        SafetyAlert?.Invoke(this, new SafetyAlertEventArgs(message, level));
    }
}
