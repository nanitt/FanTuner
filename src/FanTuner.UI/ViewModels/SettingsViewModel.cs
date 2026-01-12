using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FanTuner.Core.IPC;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.UI.ViewModels;

/// <summary>
/// Settings view ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly MainViewModel _mainViewModel;
    private readonly PipeClient _pipeClient;

    // Settings properties
    [ObservableProperty]
    private int _pollIntervalMs = 1000;

    [ObservableProperty]
    private float _emergencyCpuTemp = 95;

    [ObservableProperty]
    private float _emergencyGpuTemp = 90;

    [ObservableProperty]
    private float _emergencyHysteresis = 5;

    [ObservableProperty]
    private float _defaultMinFanPercent = 20;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private bool _enableFileLogging = true;

    [ObservableProperty]
    private string _logLevel = "Info";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // Service status
    [ObservableProperty]
    private bool _isServiceRunning;

    [ObservableProperty]
    private string _serviceVersion = "Unknown";

    [ObservableProperty]
    private int _serviceUptime;

    public bool IsConnected => _mainViewModel.IsConnected;

    public string[] AvailableThemes { get; } = { "Dark", "Light" };
    public string[] AvailableLogLevels { get; } = { "Debug", "Info", "Warning", "Error" };
    public int[] AvailablePollIntervals { get; } = { 500, 1000, 2000, 5000 };

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        MainViewModel mainViewModel,
        PipeClient pipeClient)
    {
        _logger = logger;
        _mainViewModel = mainViewModel;
        _pipeClient = pipeClient;

        _mainViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Configuration))
            {
                LoadFromConfiguration();
            }
        };

        // Track changes
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(HasUnsavedChanges) &&
                e.PropertyName != nameof(IsLoading) &&
                e.PropertyName != nameof(ErrorMessage) &&
                e.PropertyName != nameof(HasError))
            {
                HasUnsavedChanges = true;
            }
        };
    }

    public void Initialize()
    {
        LoadFromConfiguration();
        _ = RefreshServiceStatusAsync();
    }

    private void LoadFromConfiguration()
    {
        var config = _mainViewModel.Configuration;
        if (config == null) return;

        PollIntervalMs = config.PollIntervalMs;
        EmergencyCpuTemp = config.EmergencyCpuTemp;
        EmergencyGpuTemp = config.EmergencyGpuTemp;
        EmergencyHysteresis = config.EmergencyHysteresis;
        DefaultMinFanPercent = config.DefaultMinFanPercent;
        StartMinimized = config.StartMinimized;
        MinimizeToTray = config.MinimizeToTray;
        Theme = config.Theme;
        EnableFileLogging = config.EnableFileLogging;
        LogLevel = config.LogLevel;

        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_mainViewModel.Configuration == null || !IsConnected) return;

        IsLoading = true;
        ClearError();

        try
        {
            // Update configuration
            _mainViewModel.Configuration.PollIntervalMs = PollIntervalMs;
            _mainViewModel.Configuration.EmergencyCpuTemp = EmergencyCpuTemp;
            _mainViewModel.Configuration.EmergencyGpuTemp = EmergencyGpuTemp;
            _mainViewModel.Configuration.EmergencyHysteresis = EmergencyHysteresis;
            _mainViewModel.Configuration.DefaultMinFanPercent = DefaultMinFanPercent;
            _mainViewModel.Configuration.StartMinimized = StartMinimized;
            _mainViewModel.Configuration.MinimizeToTray = MinimizeToTray;
            _mainViewModel.Configuration.Theme = Theme;
            _mainViewModel.Configuration.EnableFileLogging = EnableFileLogging;
            _mainViewModel.Configuration.LogLevel = LogLevel;

            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success == true)
            {
                HasUnsavedChanges = false;
                _logger.LogInformation("Settings saved successfully");
            }
            else
            {
                SetError("Failed to save settings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = AppConfiguration.CreateDefault();

        PollIntervalMs = defaults.PollIntervalMs;
        EmergencyCpuTemp = defaults.EmergencyCpuTemp;
        EmergencyGpuTemp = defaults.EmergencyGpuTemp;
        EmergencyHysteresis = defaults.EmergencyHysteresis;
        DefaultMinFanPercent = defaults.DefaultMinFanPercent;
        StartMinimized = defaults.StartMinimized;
        MinimizeToTray = defaults.MinimizeToTray;
        Theme = defaults.Theme;
        EnableFileLogging = defaults.EnableFileLogging;
        LogLevel = defaults.LogLevel;
    }

    [RelayCommand]
    private async Task RefreshServiceStatusAsync()
    {
        if (!IsConnected) return;

        try
        {
            var status = await _pipeClient.GetStatusAsync();
            if (status != null)
            {
                IsServiceRunning = status.IsRunning;
                ServiceVersion = status.Version;
                ServiceUptime = status.UptimeSeconds;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service status");
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FanTuner", "logs");

        if (Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FanTuner");

        if (Directory.Exists(configDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = configDir,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        // Would use a file dialog here
        _logger.LogInformation("Export config requested");
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        // Would use a file dialog here
        _logger.LogInformation("Import config requested");
    }
}
