using System.Text.Json;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.Services;

/// <summary>
/// Manages application configuration persistence
/// </summary>
public class ConfigurationManager
{
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly string _backupDirectory;
    private readonly object _lock = new();
    private AppConfiguration _currentConfig;

    public event EventHandler<AppConfiguration>? ConfigurationChanged;

    /// <summary>
    /// Current configuration (read-only snapshot)
    /// </summary>
    public AppConfiguration Current
    {
        get
        {
            lock (_lock)
            {
                return _currentConfig;
            }
        }
    }

    public ConfigurationManager(ILogger<ConfigurationManager> logger, string? configPath = null)
    {
        _logger = logger;

        // Default to ProgramData for service, or AppData for user
        _configDirectory = configPath ?? GetDefaultConfigDirectory();
        _configFilePath = Path.Combine(_configDirectory, "config.json");
        _backupDirectory = Path.Combine(_configDirectory, "backups");

        _currentConfig = AppConfiguration.CreateDefault();
    }

    /// <summary>
    /// Load configuration from disk
    /// </summary>
    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists();

            if (!File.Exists(_configFilePath))
            {
                _logger.LogInformation("No config file found, creating default at {Path}", _configFilePath);
                await SaveAsync(_currentConfig, cancellationToken);
                return _currentConfig;
            }

            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, AppConfiguration.JsonOptions);

            if (config == null)
            {
                _logger.LogWarning("Config file was null, using defaults");
                return _currentConfig;
            }

            // Validate and migrate if needed
            config = ValidateAndMigrate(config);

            lock (_lock)
            {
                _currentConfig = config;
            }

            _logger.LogInformation("Configuration loaded from {Path}", _configFilePath);
            return config;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Config file corrupted, backing up and using defaults");
            await BackupCorruptConfigAsync(cancellationToken);

            lock (_lock)
            {
                _currentConfig = AppConfiguration.CreateDefault();
            }

            await SaveAsync(_currentConfig, cancellationToken);
            return _currentConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration");
            throw;
        }
    }

    /// <summary>
    /// Save configuration to disk
    /// </summary>
    public async Task SaveAsync(AppConfiguration config, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureDirectoryExists();

            // Validate before saving
            if (!config.IsValid)
            {
                _logger.LogWarning("Attempted to save invalid configuration");
                throw new InvalidOperationException("Configuration is invalid");
            }

            var json = JsonSerializer.Serialize(config, AppConfiguration.JsonOptions);

            // Write to temp file first, then rename (atomic operation)
            var tempPath = _configFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);

            // Backup existing config
            if (File.Exists(_configFilePath))
            {
                var backupPath = Path.Combine(_backupDirectory, $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                Directory.CreateDirectory(_backupDirectory);
                File.Copy(_configFilePath, backupPath, overwrite: true);

                // Keep only last 10 backups
                CleanupOldBackups(10);
            }

            File.Move(tempPath, _configFilePath, overwrite: true);

            lock (_lock)
            {
                _currentConfig = config;
            }

            _logger.LogDebug("Configuration saved to {Path}", _configFilePath);
            ConfigurationChanged?.Invoke(this, config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    /// <summary>
    /// Update specific settings without replacing entire config
    /// </summary>
    public async Task UpdateAsync(Action<AppConfiguration> updateAction, CancellationToken cancellationToken = default)
    {
        AppConfiguration config;
        lock (_lock)
        {
            config = CloneConfig(_currentConfig);
        }

        updateAction(config);
        await SaveAsync(config, cancellationToken);
    }

    /// <summary>
    /// Add or update a curve
    /// </summary>
    public async Task SaveCurveAsync(FanCurve curve, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(config =>
        {
            var existing = config.Curves.FindIndex(c => c.Id == curve.Id);
            if (existing >= 0)
            {
                config.Curves[existing] = curve;
            }
            else
            {
                config.Curves.Add(curve);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Delete a curve
    /// </summary>
    public async Task DeleteCurveAsync(string curveId, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(config =>
        {
            config.Curves.RemoveAll(c => c.Id == curveId);

            // Remove references from profiles
            foreach (var profile in config.Profiles)
            {
                foreach (var assignment in profile.FanAssignments.Values)
                {
                    if (assignment.CurveId == curveId)
                    {
                        assignment.CurveId = null;
                        assignment.Mode = FanControlMode.Auto;
                    }
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Add or update a profile
    /// </summary>
    public async Task SaveProfileAsync(FanProfile profile, CancellationToken cancellationToken = default)
    {
        profile.ModifiedAt = DateTime.UtcNow;

        await UpdateAsync(config =>
        {
            var existing = config.Profiles.FindIndex(p => p.Id == profile.Id);
            if (existing >= 0)
            {
                config.Profiles[existing] = profile;
            }
            else
            {
                config.Profiles.Add(profile);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Delete a profile
    /// </summary>
    public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(config =>
        {
            var profile = config.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile?.IsDefault == true)
            {
                throw new InvalidOperationException("Cannot delete the default profile");
            }

            config.Profiles.RemoveAll(p => p.Id == profileId);

            // If active profile was deleted, switch to default
            if (config.ActiveProfileId == profileId)
            {
                config.ActiveProfileId = config.Profiles.FirstOrDefault(p => p.IsDefault)?.Id
                    ?? config.Profiles.FirstOrDefault()?.Id;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Set the active profile
    /// </summary>
    public async Task SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(config =>
        {
            if (!config.Profiles.Any(p => p.Id == profileId))
            {
                throw new InvalidOperationException($"Profile {profileId} not found");
            }

            config.ActiveProfileId = profileId;
        }, cancellationToken);
    }

    /// <summary>
    /// Reset to default configuration
    /// </summary>
    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resetting configuration to defaults");
        await SaveAsync(AppConfiguration.CreateDefault(), cancellationToken);
    }

    /// <summary>
    /// Export configuration to a file
    /// </summary>
    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(_currentConfig, AppConfiguration.JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogInformation("Configuration exported to {Path}", filePath);
    }

    /// <summary>
    /// Import configuration from a file
    /// </summary>
    public async Task ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var config = JsonSerializer.Deserialize<AppConfiguration>(json, AppConfiguration.JsonOptions);

        if (config == null)
        {
            throw new InvalidOperationException("Invalid configuration file");
        }

        config = ValidateAndMigrate(config);
        await SaveAsync(config, cancellationToken);
        _logger.LogInformation("Configuration imported from {Path}", filePath);
    }

    private static string GetDefaultConfigDirectory()
    {
        // Use ProgramData for system-wide config (service)
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(programData, "FanTuner");
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
            _logger.LogDebug("Created config directory: {Path}", _configDirectory);
        }
    }

    private async Task BackupCorruptConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                Directory.CreateDirectory(_backupDirectory);
                var corruptPath = Path.Combine(_backupDirectory, $"config_corrupt_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Move(_configFilePath, corruptPath);
                _logger.LogWarning("Corrupt config backed up to {Path}", corruptPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup corrupt config");
        }
    }

    private void CleanupOldBackups(int keepCount)
    {
        try
        {
            if (!Directory.Exists(_backupDirectory)) return;

            var backups = Directory.GetFiles(_backupDirectory, "config_*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(keepCount)
                .ToList();

            foreach (var backup in backups)
            {
                backup.Delete();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old backups");
        }
    }

    private AppConfiguration ValidateAndMigrate(AppConfiguration config)
    {
        // Ensure we have at least one profile
        if (config.Profiles.Count == 0)
        {
            config.Profiles.Add(FanProfile.CreateDefault());
        }

        // Ensure we have a default profile
        if (!config.Profiles.Any(p => p.IsDefault))
        {
            config.Profiles[0].IsDefault = true;
        }

        // Ensure active profile exists
        if (string.IsNullOrEmpty(config.ActiveProfileId) ||
            !config.Profiles.Any(p => p.Id == config.ActiveProfileId))
        {
            config.ActiveProfileId = config.Profiles.First(p => p.IsDefault).Id;
        }

        // Validate curves
        config.Curves = config.Curves
            .Where(c => CurveEngine.ValidateCurve(c).IsValid)
            .ToList();

        // Ensure we have at least one curve
        if (config.Curves.Count == 0)
        {
            config.Curves.Add(FanCurve.CreateDefault());
        }

        return config;
    }

    private static AppConfiguration CloneConfig(AppConfiguration config)
    {
        var json = JsonSerializer.Serialize(config, AppConfiguration.JsonOptions);
        return JsonSerializer.Deserialize<AppConfiguration>(json, AppConfiguration.JsonOptions)!;
    }
}
