using System.Text.Json;
using System.Text.Json.Serialization;
using FanTuner.Core.Models;

namespace FanTuner.Core.IPC;

/// <summary>
/// Pipe constants
/// </summary>
public static class PipeConstants
{
    public const string PipeName = "FanTuner";
    public const int MaxMessageSize = 1024 * 1024; // 1MB
    public const int ConnectionTimeout = 5000; // 5 seconds
    public const int ReadTimeout = 30000; // 30 seconds
}

/// <summary>
/// Base class for all pipe messages
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GetStatusRequest), "getStatus")]
[JsonDerivedType(typeof(GetSensorsRequest), "getSensors")]
[JsonDerivedType(typeof(GetFansRequest), "getFans")]
[JsonDerivedType(typeof(GetConfigRequest), "getConfig")]
[JsonDerivedType(typeof(SetConfigRequest), "setConfig")]
[JsonDerivedType(typeof(SetFanSpeedRequest), "setFanSpeed")]
[JsonDerivedType(typeof(SetProfileRequest), "setProfile")]
[JsonDerivedType(typeof(SubscribeSensorsRequest), "subscribeSensors")]
[JsonDerivedType(typeof(UnsubscribeSensorsRequest), "unsubscribeSensors")]
[JsonDerivedType(typeof(StatusResponse), "statusResponse")]
[JsonDerivedType(typeof(SensorsResponse), "sensorsResponse")]
[JsonDerivedType(typeof(FansResponse), "fansResponse")]
[JsonDerivedType(typeof(ConfigResponse), "configResponse")]
[JsonDerivedType(typeof(ErrorResponse), "errorResponse")]
[JsonDerivedType(typeof(AckResponse), "ackResponse")]
[JsonDerivedType(typeof(SensorUpdateNotification), "sensorUpdate")]
public abstract class PipeMessage
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Serialize() => JsonSerializer.Serialize(this, GetType(), JsonOptions);

    public static PipeMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);
    }
}

#region Request Messages

/// <summary>
/// Request service status
/// </summary>
public class GetStatusRequest : PipeMessage { }

/// <summary>
/// Request current sensor readings
/// </summary>
public class GetSensorsRequest : PipeMessage { }

/// <summary>
/// Request fan device list
/// </summary>
public class GetFansRequest : PipeMessage { }

/// <summary>
/// Request current configuration
/// </summary>
public class GetConfigRequest : PipeMessage { }

/// <summary>
/// Update configuration
/// </summary>
public class SetConfigRequest : PipeMessage
{
    public AppConfiguration? Config { get; set; }
}

/// <summary>
/// Set a specific fan's speed
/// </summary>
public class SetFanSpeedRequest : PipeMessage
{
    public string FanIdKey { get; set; } = string.Empty;
    public float Percent { get; set; }
}

/// <summary>
/// Change active profile
/// </summary>
public class SetProfileRequest : PipeMessage
{
    public string ProfileId { get; set; } = string.Empty;
}

/// <summary>
/// Subscribe to sensor updates
/// </summary>
public class SubscribeSensorsRequest : PipeMessage
{
    public int IntervalMs { get; set; } = 1000;
}

/// <summary>
/// Unsubscribe from sensor updates
/// </summary>
public class UnsubscribeSensorsRequest : PipeMessage { }

#endregion

#region Response Messages

/// <summary>
/// Service status response
/// </summary>
public class StatusResponse : PipeMessage
{
    public bool IsRunning { get; set; }
    public string Version { get; set; } = "1.0.0";
    public int UptimeSeconds { get; set; }
    public bool EmergencyModeActive { get; set; }
    public string? EmergencyReason { get; set; }
    public string? ActiveProfileName { get; set; }
    public string? ActiveProfileId { get; set; }
    public List<string> Warnings { get; set; } = new();
    public int ConnectedClients { get; set; }
}

/// <summary>
/// Sensor readings response
/// </summary>
public class SensorsResponse : PipeMessage
{
    public List<SensorReading> Sensors { get; set; } = new();
}

/// <summary>
/// Fan devices response
/// </summary>
public class FansResponse : PipeMessage
{
    public List<FanDevice> Fans { get; set; } = new();
}

/// <summary>
/// Configuration response
/// </summary>
public class ConfigResponse : PipeMessage
{
    public AppConfiguration? Config { get; set; }
}

/// <summary>
/// Error response
/// </summary>
public class ErrorResponse : PipeMessage
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? OriginalRequestId { get; set; }
}

/// <summary>
/// Simple acknowledgment response
/// </summary>
public class AckResponse : PipeMessage
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? OriginalRequestId { get; set; }
}

/// <summary>
/// Push notification with sensor updates
/// </summary>
public class SensorUpdateNotification : PipeMessage
{
    public List<SensorReading> Sensors { get; set; } = new();
    public List<FanDevice> Fans { get; set; } = new();
    public bool EmergencyModeActive { get; set; }
}

#endregion
