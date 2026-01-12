using FanTuner.Core.Models;

namespace FanTuner.Core.Services;

/// <summary>
/// Engine for calculating fan speeds from temperature curves
/// </summary>
public static class CurveEngine
{
    /// <summary>
    /// Calculate fan percentage for given temperature using curve points.
    /// Uses smooth cosine interpolation between points.
    /// </summary>
    /// <param name="curve">The fan curve definition</param>
    /// <param name="temperature">Current temperature reading</param>
    /// <param name="lastOutput">Previous output value (for hysteresis)</param>
    /// <returns>Calculated fan percentage (0-100)</returns>
    public static float Interpolate(FanCurve curve, float temperature, float? lastOutput = null)
    {
        if (curve.Points.Count == 0)
            return curve.MinPercent;

        if (curve.Points.Count == 1)
            return Math.Clamp(curve.Points[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        var sorted = curve.Points.OrderBy(p => p.Temperature).ToList();

        // Below first point - use minimum
        if (temperature <= sorted[0].Temperature)
            return Math.Clamp(sorted[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        // Above last point - use maximum
        if (temperature >= sorted[^1].Temperature)
            return Math.Clamp(sorted[^1].FanPercent, curve.MinPercent, curve.MaxPercent);

        // Find surrounding points and interpolate
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (temperature >= sorted[i].Temperature && temperature <= sorted[i + 1].Temperature)
            {
                float t = (temperature - sorted[i].Temperature) /
                         (sorted[i + 1].Temperature - sorted[i].Temperature);

                // Cosine interpolation for smooth transitions
                float smoothT = (1 - MathF.Cos(t * MathF.PI)) / 2;

                float result = sorted[i].FanPercent +
                              (sorted[i + 1].FanPercent - sorted[i].FanPercent) * smoothT;

                // Apply hysteresis if we have previous output
                if (lastOutput.HasValue && curve.Hysteresis > 0)
                {
                    result = ApplyHysteresis(result, lastOutput.Value, curve.Hysteresis);
                }

                return Math.Clamp(result, curve.MinPercent, curve.MaxPercent);
            }
        }

        return curve.MinPercent;
    }

    /// <summary>
    /// Linear interpolation (alternative to cosine for faster response)
    /// </summary>
    public static float InterpolateLinear(FanCurve curve, float temperature, float? lastOutput = null)
    {
        if (curve.Points.Count == 0)
            return curve.MinPercent;

        if (curve.Points.Count == 1)
            return Math.Clamp(curve.Points[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        var sorted = curve.Points.OrderBy(p => p.Temperature).ToList();

        if (temperature <= sorted[0].Temperature)
            return Math.Clamp(sorted[0].FanPercent, curve.MinPercent, curve.MaxPercent);

        if (temperature >= sorted[^1].Temperature)
            return Math.Clamp(sorted[^1].FanPercent, curve.MinPercent, curve.MaxPercent);

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (temperature >= sorted[i].Temperature && temperature <= sorted[i + 1].Temperature)
            {
                float t = (temperature - sorted[i].Temperature) /
                         (sorted[i + 1].Temperature - sorted[i].Temperature);

                float result = sorted[i].FanPercent +
                              (sorted[i + 1].FanPercent - sorted[i].FanPercent) * t;

                if (lastOutput.HasValue && curve.Hysteresis > 0)
                {
                    result = ApplyHysteresis(result, lastOutput.Value, curve.Hysteresis);
                }

                return Math.Clamp(result, curve.MinPercent, curve.MaxPercent);
            }
        }

        return curve.MinPercent;
    }

    /// <summary>
    /// Apply hysteresis to prevent rapid fan speed changes
    /// Only change if difference exceeds threshold
    /// </summary>
    private static float ApplyHysteresis(float target, float current, float hysteresis)
    {
        if (MathF.Abs(target - current) < hysteresis)
            return current;
        return target;
    }

    /// <summary>
    /// Apply response time smoothing to fan speed changes
    /// </summary>
    /// <param name="currentSpeed">Current fan speed</param>
    /// <param name="targetSpeed">Target fan speed</param>
    /// <param name="responseTime">Response time in seconds</param>
    /// <param name="deltaTime">Time since last update in seconds</param>
    /// <returns>Smoothed fan speed</returns>
    public static float ApplyResponseTime(float currentSpeed, float targetSpeed, float responseTime, float deltaTime)
    {
        if (responseTime <= 0)
            return targetSpeed;

        // Calculate how much we can change based on elapsed time
        float maxChange = 100f * deltaTime / responseTime;

        float diff = targetSpeed - currentSpeed;
        if (MathF.Abs(diff) <= maxChange)
            return targetSpeed;

        return currentSpeed + MathF.Sign(diff) * maxChange;
    }

    /// <summary>
    /// Validate a curve for correctness
    /// </summary>
    public static (bool IsValid, string? Error) ValidateCurve(FanCurve curve)
    {
        if (curve.Points.Count < 2)
            return (false, "Curve must have at least 2 points");

        if (curve.MinPercent < 0 || curve.MinPercent > 100)
            return (false, "Minimum percent must be between 0 and 100");

        if (curve.MaxPercent < 0 || curve.MaxPercent > 100)
            return (false, "Maximum percent must be between 0 and 100");

        if (curve.MinPercent > curve.MaxPercent)
            return (false, "Minimum percent cannot be greater than maximum");

        foreach (var point in curve.Points)
        {
            if (point.Temperature < -40 || point.Temperature > 150)
                return (false, $"Temperature {point.Temperature}°C is out of valid range (-40 to 150)");

            if (point.FanPercent < 0 || point.FanPercent > 100)
                return (false, $"Fan percent {point.FanPercent}% is out of valid range (0 to 100)");
        }

        // Check for duplicate temperatures
        var temps = curve.Points.Select(p => p.Temperature).ToList();
        if (temps.Distinct().Count() != temps.Count)
            return (false, "Curve contains duplicate temperature points");

        return (true, null);
    }

    /// <summary>
    /// Normalize curve points (sort by temperature, remove duplicates)
    /// </summary>
    public static FanCurve NormalizeCurve(FanCurve curve)
    {
        var normalized = new FanCurve
        {
            Id = curve.Id,
            Name = curve.Name,
            SourceSensor = curve.SourceSensor,
            MinPercent = curve.MinPercent,
            MaxPercent = curve.MaxPercent,
            Hysteresis = curve.Hysteresis,
            ResponseTime = curve.ResponseTime,
            Points = curve.Points
                .GroupBy(p => p.Temperature)
                .Select(g => g.First())
                .OrderBy(p => p.Temperature)
                .ToList()
        };

        return normalized;
    }
}
