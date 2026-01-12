using System.Text.Json;
using FanTuner.Core.Models;
using FanTuner.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FanTuner.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly string _testConfigPath;
    private readonly ConfigurationManager _configManager;

    public ConfigurationTests()
    {
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"FanTunerTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testConfigPath);

        _configManager = new ConfigurationManager(
            NullLogger<ConfigurationManager>.Instance,
            _testConfigPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testConfigPath, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task LoadAsync_NoExistingFile_CreatesDefault()
    {
        var config = await _configManager.LoadAsync();

        config.Should().NotBeNull();
        config.Version.Should().Be("1.0");
        config.Profiles.Should().NotBeEmpty();
        config.Curves.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SaveAsync_CreatesFile()
    {
        var config = AppConfiguration.CreateDefault();
        config.PollIntervalMs = 2000;

        await _configManager.SaveAsync(config);

        var filePath = Path.Combine(_testConfigPath, "config.json");
        File.Exists(filePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(filePath);
        json.Should().Contain("pollIntervalMs");
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_LoadsCorrectly()
    {
        // Save first
        var originalConfig = AppConfiguration.CreateDefault();
        originalConfig.PollIntervalMs = 3000;
        originalConfig.Theme = "Light";
        await _configManager.SaveAsync(originalConfig);

        // Create new manager and load
        var newManager = new ConfigurationManager(
            NullLogger<ConfigurationManager>.Instance,
            _testConfigPath);

        var loadedConfig = await newManager.LoadAsync();

        loadedConfig.PollIntervalMs.Should().Be(3000);
        loadedConfig.Theme.Should().Be("Light");
    }

    [Fact]
    public async Task SaveCurveAsync_AddsCurve()
    {
        await _configManager.LoadAsync();

        var newCurve = new FanCurve
        {
            Name = "Test Curve",
            Points = new List<CurvePoint>
            {
                new(30f, 30f),
                new(60f, 60f)
            }
        };

        await _configManager.SaveCurveAsync(newCurve);

        var config = _configManager.Current;
        config.Curves.Should().Contain(c => c.Name == "Test Curve");
    }

    [Fact]
    public async Task SaveCurveAsync_UpdatesExistingCurve()
    {
        var config = await _configManager.LoadAsync();
        var existingCurve = config.Curves.First();
        var originalName = existingCurve.Name;

        existingCurve.Name = "Updated Name";
        await _configManager.SaveCurveAsync(existingCurve);

        var updatedConfig = _configManager.Current;
        var curve = updatedConfig.Curves.First(c => c.Id == existingCurve.Id);
        curve.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteCurveAsync_RemovesCurve()
    {
        var config = await _configManager.LoadAsync();

        // Add an extra curve to delete
        var curveToDelete = new FanCurve { Name = "ToDelete" };
        config.Curves.Add(curveToDelete);
        await _configManager.SaveAsync(config);

        await _configManager.DeleteCurveAsync(curveToDelete.Id);

        _configManager.Current.Curves.Should().NotContain(c => c.Id == curveToDelete.Id);
    }

    [Fact]
    public async Task SaveProfileAsync_AddsProfile()
    {
        await _configManager.LoadAsync();

        var newProfile = new FanProfile
        {
            Name = "Gaming"
        };

        await _configManager.SaveProfileAsync(newProfile);

        _configManager.Current.Profiles.Should().Contain(p => p.Name == "Gaming");
    }

    [Fact]
    public async Task DeleteProfileAsync_DefaultProfile_Throws()
    {
        var config = await _configManager.LoadAsync();
        var defaultProfile = config.Profiles.First(p => p.IsDefault);

        Func<Task> act = async () => await _configManager.DeleteProfileAsync(defaultProfile.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*default*");
    }

    [Fact]
    public async Task SetActiveProfileAsync_ChangesActiveProfile()
    {
        var config = await _configManager.LoadAsync();

        var newProfile = new FanProfile { Name = "New" };
        config.Profiles.Add(newProfile);
        await _configManager.SaveAsync(config);

        await _configManager.SetActiveProfileAsync(newProfile.Id);

        _configManager.Current.ActiveProfileId.Should().Be(newProfile.Id);
    }

    [Fact]
    public async Task SetActiveProfileAsync_NonExistentProfile_Throws()
    {
        await _configManager.LoadAsync();

        Func<Task> act = async () => await _configManager.SetActiveProfileAsync("non-existent-id");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_UsesDefaults()
    {
        // Write corrupt JSON
        var configPath = Path.Combine(_testConfigPath, "config.json");
        await File.WriteAllTextAsync(configPath, "{ invalid json }}}");

        var config = await _configManager.LoadAsync();

        config.Should().NotBeNull();
        config.Profiles.Should().NotBeEmpty();

        // Corrupt file should be backed up
        var backupDir = Path.Combine(_testConfigPath, "backups");
        Directory.Exists(backupDir).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ModifiesAndSaves()
    {
        await _configManager.LoadAsync();

        await _configManager.UpdateAsync(config =>
        {
            config.PollIntervalMs = 5000;
            config.Theme = "Light";
        });

        _configManager.Current.PollIntervalMs.Should().Be(5000);
        _configManager.Current.Theme.Should().Be("Light");
    }

    [Fact]
    public async Task ConfigurationChanged_EventFired()
    {
        await _configManager.LoadAsync();

        var eventFired = false;
        _configManager.ConfigurationChanged += (s, e) => eventFired = true;

        await _configManager.UpdateAsync(config => config.PollIntervalMs = 2000);

        eventFired.Should().BeTrue();
    }
}

public class AppConfigurationModelTests
{
    [Fact]
    public void CreateDefault_HasValidConfiguration()
    {
        var config = AppConfiguration.CreateDefault();

        config.IsValid.Should().BeTrue();
        config.Profiles.Should().NotBeEmpty();
        config.Curves.Should().NotBeEmpty();
        config.ActiveProfileId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetActiveProfile_ReturnsActiveProfile()
    {
        var config = AppConfiguration.CreateDefault();

        var profile = config.GetActiveProfile();

        profile.Should().NotBeNull();
        profile.Id.Should().Be(config.ActiveProfileId);
    }

    [Fact]
    public void GetActiveProfile_NoActiveId_ReturnsDefault()
    {
        var config = AppConfiguration.CreateDefault();
        config.ActiveProfileId = null;

        var profile = config.GetActiveProfile();

        profile.Should().NotBeNull();
        profile.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void GetCurve_ExistingId_ReturnsCurve()
    {
        var config = AppConfiguration.CreateDefault();
        var existingCurve = config.Curves.First();

        var curve = config.GetCurve(existingCurve.Id);

        curve.Should().NotBeNull();
        curve!.Id.Should().Be(existingCurve.Id);
    }

    [Fact]
    public void GetCurve_NonExistentId_ReturnsNull()
    {
        var config = AppConfiguration.CreateDefault();

        var curve = config.GetCurve("non-existent");

        curve.Should().BeNull();
    }

    [Fact]
    public void IsValid_InvalidPollInterval_ReturnsFalse()
    {
        var config = AppConfiguration.CreateDefault();
        config.PollIntervalMs = 50; // Too low

        config.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_InvalidEmergencyTemp_ReturnsFalse()
    {
        var config = AppConfiguration.CreateDefault();
        config.EmergencyCpuTemp = 200; // Too high

        config.IsValid.Should().BeFalse();
    }

    [Fact]
    public void JsonSerialization_RoundTrip()
    {
        var original = AppConfiguration.CreateDefault();
        original.PollIntervalMs = 2500;
        original.Theme = "Light";

        var json = JsonSerializer.Serialize(original, AppConfiguration.JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AppConfiguration>(json, AppConfiguration.JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.PollIntervalMs.Should().Be(2500);
        deserialized.Theme.Should().Be("Light");
    }
}

public class FanCurveModelTests
{
    [Fact]
    public void CreateDefault_HasValidPoints()
    {
        var curve = FanCurve.CreateDefault();

        curve.IsValid.Should().BeTrue();
        curve.Points.Should().HaveCountGreaterThan(2);
    }

    [Fact]
    public void CreateQuiet_HasLowerSpeeds()
    {
        var quiet = FanCurve.CreateQuiet();
        var performance = FanCurve.CreatePerformance();

        // At same temperature point, quiet should have lower fan speed
        var quietAt50 = quiet.Points.FirstOrDefault(p => p.Temperature == 50);
        var perfAt50 = performance.Points.FirstOrDefault(p => p.Temperature == 50);

        if (quietAt50 != null && perfAt50 != null)
        {
            quietAt50.FanPercent.Should().BeLessThan(perfAt50.FanPercent);
        }
    }

    [Fact]
    public void IsValid_TooFewPoints_ReturnsFalse()
    {
        var curve = new FanCurve
        {
            Points = new List<CurvePoint> { new(50f, 50f) }
        };

        curve.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_InvalidMinMax_ReturnsFalse()
    {
        var curve = FanCurve.CreateDefault();
        curve.MinPercent = 80f;
        curve.MaxPercent = 50f;

        curve.IsValid.Should().BeFalse();
    }
}

public class FanProfileModelTests
{
    [Fact]
    public void CreateDefault_IsMarkedDefault()
    {
        var profile = FanProfile.CreateDefault();

        profile.IsDefault.Should().BeTrue();
        profile.Name.Should().Be("Default");
    }

    [Fact]
    public void GetOrCreateAssignment_NewFan_CreatesAutoAssignment()
    {
        var profile = new FanProfile();

        var assignment = profile.GetOrCreateAssignment("test-fan-id");

        assignment.Should().NotBeNull();
        assignment.FanIdKey.Should().Be("test-fan-id");
        assignment.Mode.Should().Be(FanControlMode.Auto);
    }

    [Fact]
    public void GetOrCreateAssignment_ExistingFan_ReturnsExisting()
    {
        var profile = new FanProfile();
        var existing = new FanAssignment
        {
            FanIdKey = "test-fan-id",
            Mode = FanControlMode.Manual,
            ManualPercent = 75f
        };
        profile.FanAssignments["test-fan-id"] = existing;

        var assignment = profile.GetOrCreateAssignment("test-fan-id");

        assignment.Mode.Should().Be(FanControlMode.Manual);
        assignment.ManualPercent.Should().Be(75f);
    }
}
