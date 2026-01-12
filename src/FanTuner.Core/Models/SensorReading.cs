using System.Text.Json.Serialization;

namespace FanTuner.Core.Models;

/// <summary>
/// Unique identifier for a sensor
/// </summary>
public sealed record SensorId
{
    public string HardwareId { get; init; } = string.Empty;
    public string SensorName { get; init; } = string.Empty;
    public SensorType Type { get; init; }

    [JsonIgnore]
    public string UniqueKey => $"{HardwareId}:{SensorName}:{Type}";

    public SensorId() { }

    public SensorId(string hardwareId, string sensorName, SensorType type)
    {
        HardwareId = hardwareId;
        SensorName = sensorName;
        Type = type;
    }
}

/// <summary>
/// A single sensor reading with metadata
/// </summary>
public sealed class SensorReading
{
    public SensorId Id { get; init; } = new();
    public string DisplayName { get; init; } = string.Empty;
    public string HardwareName { get; init; } = string.Empty;
    public HardwareType HardwareType { get; init; }
    public float Value { get; init; }
    public float? Min { get; init; }
    public float? Max { get; init; }
    public string Unit { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsStale { get; init; }

    /// <summary>
    /// Formatted value string for display
    /// </summary>
    [JsonIgnore]
    public string FormattedValue => Id.Type switch
    {
        SensorType.Temperature => $"{Value:F1}°C",
        SensorType.Fan => $"{Value:F0} RPM",
        SensorType.Load => $"{Value:F1}%",
        SensorType.Voltage => $"{Value:F3} V",
        SensorType.Clock => $"{Value:F0} MHz",
        SensorType.Power => $"{Value:F1} W",
        SensorType.Control => $"{Value:F1}%",
        _ => $"{Value:F2} {Unit}"
    };
}
