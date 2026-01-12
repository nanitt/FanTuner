using System.Text.Json;
using System.Text.Json.Serialization;

namespace FanTuner.Core.Models;

/// <summary>
/// Application configuration that persists to disk
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>
    /// Config file version for migrations
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Sensor polling interval in milliseconds
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Emergency CPU temperature threshold (forces 100% fans)
    /// </summary>
    public float EmergencyCpuTemp { get; set; } = 95f;

    /// <summary>
    /// Emergency GPU temperature threshold (forces 100% fans)
    /// </summary>
    public float EmergencyGpuTemp { get; set; } = 90f;

    /// <summary>
    /// Temperature drop before exiting emergency mode
    /// </summary>
    public float EmergencyHysteresis { get; set; } = 5f;

    /// <summary>
    /// Default minimum fan percentage (prevents stall)
    /// </summary>
    public float DefaultMinFanPercent { get; set; } = 20f;

    /// <summary>
    /// ID of the currently active profile
    /// </summary>
    public string? ActiveProfileId { get; set; }

    /// <summary>
    /// Start UI minimized to tray
    /// </summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Minimize to tray instead of closing
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// UI theme (Dark/Light)
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Enable logging to file
    /// </summary>
    public bool EnableFileLogging { get; set; } = true;

    /// <summary>
    /// Log level (Debug, Info, Warning, Error)
    /// </summary>
    public string LogLevel { get; set; } = "Info";

    /// <summary>
    /// Maximum log file size in MB before rotation
    /// </summary>
    public int MaxLogFileSizeMb { get; set; } = 10;

    /// <summary>
    /// Number of log files to keep
    /// </summary>
    public int MaxLogFiles { get; set; } = 5;

    /// <summary>
    /// Defined fan curves
    /// </summary>
    public List<FanCurve> Curves { get; set; } = new();

    /// <summary>
    /// Defined profiles
    /// </summary>
    public List<FanProfile> Profiles { get; set; } = new();

    /// <summary>
    /// List of detected conflicting software
    /// </summary>
    public List<string> ConflictingSoftwareWarnings { get; set; } = new();

    /// <summary>
    /// Last time hardware was enumerated
    /// </summary>
    public DateTime? LastHardwareEnumeration { get; set; }

    /// <summary>
    /// Create default configuration with sensible values
    /// </summary>
    public static AppConfiguration CreateDefault()
    {
        var config = new AppConfiguration();

        // Add default curves
        var defaultCurve = FanCurve.CreateDefault("Balanced");
        var quietCurve = FanCurve.CreateQuiet();
        var perfCurve = FanCurve.CreatePerformance();

        config.Curves.Add(defaultCurve);
        config.Curves.Add(quietCurve);
        config.Curves.Add(perfCurve);

        // Add default profile
        var defaultProfile = FanProfile.CreateDefault();
        config.Profiles.Add(defaultProfile);
        config.ActiveProfileId = defaultProfile.Id;

        return config;
    }

    /// <summary>
    /// Get the active profile, or the default profile, or create a new default
    /// </summary>
    public FanProfile GetActiveProfile()
    {
        FanProfile? profile = null;

        if (!string.IsNullOrEmpty(ActiveProfileId))
        {
            profile = Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);
        }

        profile ??= Profiles.FirstOrDefault(p => p.IsDefault);

        if (profile == null)
        {
            profile = FanProfile.CreateDefault();
            Profiles.Add(profile);
            ActiveProfileId = profile.Id;
        }

        return profile;
    }

    /// <summary>
    /// Get a curve by ID
    /// </summary>
    public FanCurve? GetCurve(string curveId)
    {
        return Curves.FirstOrDefault(c => c.Id == curveId);
    }

    /// <summary>
    /// Validate configuration
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        PollIntervalMs >= 100 && PollIntervalMs <= 10000 &&
        EmergencyCpuTemp >= 50 && EmergencyCpuTemp <= 120 &&
        EmergencyGpuTemp >= 50 && EmergencyGpuTemp <= 120 &&
        DefaultMinFanPercent >= 0 && DefaultMinFanPercent <= 50;

    /// <summary>
    /// JSON serialization options
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
