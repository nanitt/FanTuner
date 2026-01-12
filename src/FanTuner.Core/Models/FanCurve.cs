using System.Text.Json.Serialization;

namespace FanTuner.Core.Models;

/// <summary>
/// A single point on a fan curve
/// </summary>
public sealed class CurvePoint
{
    public float Temperature { get; set; }
    public float FanPercent { get; set; }

    public CurvePoint() { }

    public CurvePoint(float temperature, float fanPercent)
    {
        Temperature = temperature;
        FanPercent = fanPercent;
    }
}

/// <summary>
/// Defines a temperature-to-fan-speed curve
/// </summary>
public sealed class FanCurve
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default Curve";

    /// <summary>
    /// The sensor to use as temperature source
    /// </summary>
    public SensorId? SourceSensor { get; set; }

    /// <summary>
    /// Curve points (temperature -> fan %)
    /// </summary>
    public List<CurvePoint> Points { get; set; } = new();

    /// <summary>
    /// Minimum fan percentage (prevents stall)
    /// </summary>
    public float MinPercent { get; set; } = 20f;

    /// <summary>
    /// Maximum fan percentage
    /// </summary>
    public float MaxPercent { get; set; } = 100f;

    /// <summary>
    /// Temperature hysteresis to prevent rapid changes (degrees)
    /// </summary>
    public float Hysteresis { get; set; } = 2f;

    /// <summary>
    /// Response time - how quickly to ramp up/down (seconds)
    /// </summary>
    public float ResponseTime { get; set; } = 1f;

    /// <summary>
    /// Create a default curve with sensible values
    /// </summary>
    public static FanCurve CreateDefault(string name = "Default")
    {
        return new FanCurve
        {
            Name = name,
            MinPercent = 20f,
            MaxPercent = 100f,
            Hysteresis = 2f,
            Points = new List<CurvePoint>
            {
                new(30f, 20f),   // 30°C -> 20%
                new(40f, 30f),   // 40°C -> 30%
                new(50f, 40f),   // 50°C -> 40%
                new(60f, 60f),   // 60°C -> 60%
                new(70f, 80f),   // 70°C -> 80%
                new(80f, 100f)   // 80°C -> 100%
            }
        };
    }

    /// <summary>
    /// Create a quiet curve (lower fan speeds)
    /// </summary>
    public static FanCurve CreateQuiet()
    {
        return new FanCurve
        {
            Name = "Quiet",
            MinPercent = 15f,
            MaxPercent = 80f,
            Hysteresis = 3f,
            Points = new List<CurvePoint>
            {
                new(30f, 15f),
                new(50f, 25f),
                new(65f, 40f),
                new(75f, 60f),
                new(85f, 80f)
            }
        };
    }

    /// <summary>
    /// Create a performance curve (aggressive cooling)
    /// </summary>
    public static FanCurve CreatePerformance()
    {
        return new FanCurve
        {
            Name = "Performance",
            MinPercent = 30f,
            MaxPercent = 100f,
            Hysteresis = 1f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(40f, 50f),
                new(50f, 70f),
                new(60f, 85f),
                new(70f, 100f)
            }
        };
    }

    /// <summary>
    /// Validates the curve configuration
    /// </summary>
    [JsonIgnore]
    public bool IsValid => Points.Count >= 2 &&
                           Points.All(p => p.Temperature >= 0 && p.Temperature <= 150) &&
                           Points.All(p => p.FanPercent >= 0 && p.FanPercent <= 100) &&
                           MinPercent >= 0 && MaxPercent <= 100 && MinPercent <= MaxPercent;
}
