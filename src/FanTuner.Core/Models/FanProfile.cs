namespace FanTuner.Core.Models;

/// <summary>
/// Assignment of a fan to a control mode
/// </summary>
public sealed class FanAssignment
{
    public string FanIdKey { get; set; } = string.Empty;
    public FanControlMode Mode { get; set; } = FanControlMode.Auto;

    /// <summary>
    /// For Manual mode: fixed percentage
    /// </summary>
    public float? ManualPercent { get; set; }

    /// <summary>
    /// For Curve mode: ID of the curve to use
    /// </summary>
    public string? CurveId { get; set; }

    /// <summary>
    /// Last calculated/applied fan speed
    /// </summary>
    public float? LastAppliedPercent { get; set; }
}

/// <summary>
/// A profile containing fan assignments and settings
/// </summary>
public sealed class FanProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default Profile";
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>
    /// Fan assignments keyed by fan ID unique key
    /// </summary>
    public Dictionary<string, FanAssignment> FanAssignments { get; set; } = new();

    /// <summary>
    /// Optional: hotkey to activate this profile
    /// </summary>
    public string? Hotkey { get; set; }

    /// <summary>
    /// Created timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modified timestamp
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Create a default profile where all fans are in Auto mode
    /// </summary>
    public static FanProfile CreateDefault()
    {
        return new FanProfile
        {
            Name = "Default",
            Description = "All fans in automatic mode",
            IsDefault = true
        };
    }

    /// <summary>
    /// Get the assignment for a specific fan, or create a new Auto assignment
    /// </summary>
    public FanAssignment GetOrCreateAssignment(string fanIdKey)
    {
        if (!FanAssignments.TryGetValue(fanIdKey, out var assignment))
        {
            assignment = new FanAssignment
            {
                FanIdKey = fanIdKey,
                Mode = FanControlMode.Auto
            };
            FanAssignments[fanIdKey] = assignment;
        }
        return assignment;
    }
}
