using FanTuner.Core.Models;
using FanTuner.Core.Services;
using FluentAssertions;
using Xunit;

namespace FanTuner.Tests;

public class CurveEngineTests
{
    [Fact]
    public void Interpolate_EmptyCurve_ReturnsMinPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 25f,
            Points = new List<CurvePoint>()
        };

        var result = CurveEngine.Interpolate(curve, 50f);

        result.Should().Be(25f);
    }

    [Fact]
    public void Interpolate_SinglePoint_ReturnsThatPointClamped()
    {
        var curve = new FanCurve
        {
            MinPercent = 20f,
            MaxPercent = 100f,
            Points = new List<CurvePoint>
            {
                new(50f, 50f)
            }
        };

        var result = CurveEngine.Interpolate(curve, 60f);

        result.Should().Be(50f);
    }

    [Fact]
    public void Interpolate_BelowFirstPoint_ReturnsFirstPointPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 10f,
            MaxPercent = 100f,
            Points = new List<CurvePoint>
            {
                new(30f, 20f),
                new(60f, 60f),
                new(80f, 100f)
            }
        };

        var result = CurveEngine.Interpolate(curve, 20f);

        result.Should().Be(20f);
    }

    [Fact]
    public void Interpolate_AboveLastPoint_ReturnsLastPointPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 10f,
            MaxPercent = 100f,
            Points = new List<CurvePoint>
            {
                new(30f, 20f),
                new(60f, 60f),
                new(80f, 100f)
            }
        };

        var result = CurveEngine.Interpolate(curve, 90f);

        result.Should().Be(100f);
    }

    [Fact]
    public void Interpolate_ExactlyOnPoint_ReturnsThatPointPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 10f,
            MaxPercent = 100f,
            Points = new List<CurvePoint>
            {
                new(30f, 20f),
                new(60f, 60f),
                new(80f, 100f)
            }
        };

        var result = CurveEngine.Interpolate(curve, 60f);

        result.Should().Be(60f);
    }

    [Fact]
    public void Interpolate_BetweenPoints_ReturnsInterpolatedValue()
    {
        var curve = new FanCurve
        {
            MinPercent = 10f,
            MaxPercent = 100f,
            Hysteresis = 0f, // Disable hysteresis for this test
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        // At midpoint (45°C), should be around 45% with cosine interpolation
        var result = CurveEngine.Interpolate(curve, 45f);

        result.Should().BeApproximately(45f, 1f);
    }

    [Fact]
    public void Interpolate_ClampsToMinPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 30f,
            MaxPercent = 100f,
            Points = new List<CurvePoint>
            {
                new(30f, 10f),  // Below min
                new(60f, 60f)
            }
        };

        var result = CurveEngine.Interpolate(curve, 30f);

        result.Should().Be(30f); // Clamped to min
    }

    [Fact]
    public void Interpolate_ClampsToMaxPercent()
    {
        var curve = new FanCurve
        {
            MinPercent = 20f,
            MaxPercent = 80f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 100f)  // Above max
            }
        };

        var result = CurveEngine.Interpolate(curve, 60f);

        result.Should().Be(80f); // Clamped to max
    }

    [Theory]
    [InlineData(30f, 30f)]
    [InlineData(40f, 40f)]
    [InlineData(50f, 50f)]
    [InlineData(60f, 60f)]
    [InlineData(70f, 70f)]
    public void InterpolateLinear_LinearCurve_ReturnsExactValues(float temp, float expected)
    {
        var curve = new FanCurve
        {
            MinPercent = 0f,
            MaxPercent = 100f,
            Hysteresis = 0f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(70f, 70f)
            }
        };

        var result = CurveEngine.InterpolateLinear(curve, temp);

        result.Should().BeApproximately(expected, 0.1f);
    }

    [Fact]
    public void Interpolate_WithHysteresis_DoesNotChangeIfWithinThreshold()
    {
        var curve = new FanCurve
        {
            MinPercent = 20f,
            MaxPercent = 100f,
            Hysteresis = 5f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        // First call establishes baseline
        var result1 = CurveEngine.Interpolate(curve, 45f, lastOutput: 43f);

        // Should not change because result would be close to last output
        result1.Should().BeApproximately(43f, 0.1f);
    }

    [Fact]
    public void Interpolate_WithHysteresis_ChangesIfExceedsThreshold()
    {
        var curve = new FanCurve
        {
            MinPercent = 20f,
            MaxPercent = 100f,
            Hysteresis = 2f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        // Target ~50%, last was 30%, difference > 2%
        var result = CurveEngine.Interpolate(curve, 50f, lastOutput: 30f);

        result.Should().BeGreaterThan(40f);
    }

    [Fact]
    public void ApplyResponseTime_InstantIfZeroResponseTime()
    {
        var result = CurveEngine.ApplyResponseTime(
            currentSpeed: 30f,
            targetSpeed: 80f,
            responseTime: 0f,
            deltaTime: 1f);

        result.Should().Be(80f);
    }

    [Fact]
    public void ApplyResponseTime_GradualChange()
    {
        var result = CurveEngine.ApplyResponseTime(
            currentSpeed: 30f,
            targetSpeed: 80f,
            responseTime: 5f,
            deltaTime: 1f);

        // Should move 20% (100% / 5s) toward target
        result.Should().Be(50f);
    }

    [Fact]
    public void ApplyResponseTime_DoesNotOvershoot()
    {
        var result = CurveEngine.ApplyResponseTime(
            currentSpeed: 75f,
            targetSpeed: 80f,
            responseTime: 1f,
            deltaTime: 1f);

        result.Should().Be(80f);
    }

    [Fact]
    public void ValidateCurve_ValidCurve_ReturnsTrue()
    {
        var curve = FanCurve.CreateDefault();

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateCurve_TooFewPoints_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            Points = new List<CurvePoint> { new(30f, 30f) }
        };

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeFalse();
        error.Should().Contain("at least 2 points");
    }

    [Fact]
    public void ValidateCurve_InvalidMinPercent_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            MinPercent = -10f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeFalse();
        error.Should().Contain("Minimum percent");
    }

    [Fact]
    public void ValidateCurve_MinGreaterThanMax_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            MinPercent = 80f,
            MaxPercent = 50f,
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeFalse();
        error.Should().Contain("cannot be greater");
    }

    [Fact]
    public void ValidateCurve_InvalidTemperature_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            Points = new List<CurvePoint>
            {
                new(-100f, 30f),  // Invalid
                new(60f, 60f)
            }
        };

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeFalse();
        error.Should().Contain("out of valid range");
    }

    [Fact]
    public void ValidateCurve_DuplicateTemperatures_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            Points = new List<CurvePoint>
            {
                new(50f, 30f),
                new(50f, 60f)  // Duplicate temp
            }
        };

        var (isValid, error) = CurveEngine.ValidateCurve(curve);

        isValid.Should().BeFalse();
        error.Should().Contain("duplicate");
    }

    [Fact]
    public void NormalizeCurve_SortsAndDeduplicates()
    {
        var curve = new FanCurve
        {
            Name = "Test",
            Points = new List<CurvePoint>
            {
                new(60f, 60f),
                new(30f, 30f),
                new(60f, 65f),  // Duplicate (first wins)
                new(45f, 45f)
            }
        };

        var normalized = CurveEngine.NormalizeCurve(curve);

        normalized.Points.Should().HaveCount(3);
        normalized.Points[0].Temperature.Should().Be(30f);
        normalized.Points[1].Temperature.Should().Be(45f);
        normalized.Points[2].Temperature.Should().Be(60f);
        normalized.Points[2].FanPercent.Should().Be(60f); // First duplicate wins
    }
}
