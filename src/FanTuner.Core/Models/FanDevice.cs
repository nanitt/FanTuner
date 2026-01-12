using System.Text.Json.Serialization;

namespace FanTuner.Core.Models;

/// <summary>
/// Unique identifier for a fan
/// </summary>
public sealed record FanId
{
    public string HardwareId { get; init; } = string.Empty;
    public string FanName { get; init; } = string.Empty;
    public int Index { get; init; }

    [JsonIgnore]
    public string UniqueKey => $"{HardwareId}:{FanName}:{Index}";

    public FanId() { }

    public FanId(string hardwareId, string fanName, int index)
    {
        HardwareId = hardwareId;
        FanName = fanName;
        Index = index;
    }
}

/// <summary>
/// Represents a controllable fan device
/// </summary>
public sealed class FanDevice
{
    public FanId Id { get; init; } = new();
    public string DisplayName { get; init; } = string.Empty;
    public string HardwareName { get; init; } = string.Empty;
    public FanControlCapability Capability { get; set; } = FanControlCapability.Unknown;
    public float CurrentRpm { get; set; }
    public float? CurrentPercent { get; set; }
    public float? MinPercent { get; set; } = 0f;
    public float? MaxPercent { get; set; } = 100f;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this fan can be controlled (not just monitored)
    /// </summary>
    [JsonIgnore]
    public bool CanControl => Capability == FanControlCapability.FullControl;

    /// <summary>
    /// Status text for display
    /// </summary>
    [JsonIgnore]
    public string StatusText => Capability switch
    {
        FanControlCapability.FullControl => "Controllable",
        FanControlCapability.MonitorOnly => "Monitor Only",
        FanControlCapability.Unavailable => "Unavailable",
        _ => "Unknown"
    };
}
