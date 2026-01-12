using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FanTuner.Core.IPC;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.UI.ViewModels;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly PipeClient _pipeClient;
    private CancellationTokenSource? _updateCts;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isEmergencyMode;

    [ObservableProperty]
    private string? _emergencyReason;

    [ObservableProperty]
    private string _activeProfileName = "Default";

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private ObservableCollection<string> _warnings = new();

    // Sensor data
    [ObservableProperty]
    private ObservableCollection<SensorReading> _temperatureSensors = new();

    [ObservableProperty]
    private ObservableCollection<FanDevice> _fans = new();

    [ObservableProperty]
    private AppConfiguration? _configuration;

    // Quick stats
    [ObservableProperty]
    private float _cpuTemp;

    [ObservableProperty]
    private float _gpuTemp;

    [ObservableProperty]
    private float _maxFanRpm;

    public MainViewModel(ILogger<MainViewModel> logger, PipeClient pipeClient)
    {
        _logger = logger;
        _pipeClient = pipeClient;

        // Subscribe to connection events
        _pipeClient.ConnectionStateChanged += OnConnectionStateChanged;
        _pipeClient.SensorUpdateReceived += OnSensorUpdateReceived;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing MainViewModel");

        await ConnectAsync();

        if (IsConnected)
        {
            await LoadDataAsync();
            await StartSensorUpdatesAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ConnectionStatus = "Connecting...";

        try
        {
            var connected = await _pipeClient.ConnectAsync();

            if (connected)
            {
                _logger.LogInformation("Connected to FanTuner service");
            }
            else
            {
                SetError("Failed to connect to FanTuner service. Make sure the service is running.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to service");
            SetError($"Connection error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _updateCts?.Cancel();
        _pipeClient.Disconnect();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (!IsConnected) return;

        IsLoading = true;
        ClearError();

        try
        {
            // Get status
            var status = await _pipeClient.GetStatusAsync();
            if (status != null)
            {
                IsEmergencyMode = status.EmergencyModeActive;
                EmergencyReason = status.EmergencyReason;
                ActiveProfileName = status.ActiveProfileName ?? "Default";

                Warnings.Clear();
                foreach (var warning in status.Warnings)
                {
                    Warnings.Add(warning);
                }
            }

            // Get config
            var configResponse = await _pipeClient.GetConfigAsync();
            Configuration = configResponse?.Config;

            // Get sensors
            var sensorsResponse = await _pipeClient.GetSensorsAsync();
            if (sensorsResponse != null)
            {
                UpdateSensorData(sensorsResponse.Sensors);
            }

            // Get fans
            var fansResponse = await _pipeClient.GetFansAsync();
            if (fansResponse != null)
            {
                UpdateFanData(fansResponse.Fans);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading data");
            SetError($"Error loading data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task StartSensorUpdatesAsync()
    {
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();

        try
        {
            var subscribed = await _pipeClient.SubscribeSensorsAsync(
                Configuration?.PollIntervalMs ?? 1000,
                _updateCts.Token);

            if (subscribed)
            {
                _logger.LogDebug("Subscribed to sensor updates");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to sensor updates");
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = e.IsConnected;
            ConnectionStatus = e.IsConnected ? "Connected" : $"Disconnected: {e.Error ?? "Unknown"}";

            if (!e.IsConnected)
            {
                // Clear data on disconnect
                TemperatureSensors.Clear();
                Fans.Clear();
                Warnings.Clear();
            }
        });
    }

    private void OnSensorUpdateReceived(object? sender, SensorUpdateNotification update)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsEmergencyMode = update.EmergencyModeActive;
            UpdateSensorData(update.Sensors);
            UpdateFanData(update.Fans);
        });
    }

    private void UpdateSensorData(IEnumerable<SensorReading> sensors)
    {
        var temps = sensors.Where(s => s.Id.Type == SensorType.Temperature).ToList();

        TemperatureSensors.Clear();
        foreach (var temp in temps)
        {
            TemperatureSensors.Add(temp);
        }

        // Update quick stats
        var cpuTemps = temps.Where(s => s.HardwareType == HardwareType.Cpu);
        var gpuTemps = temps.Where(s =>
            s.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel);

        CpuTemp = cpuTemps.Any() ? cpuTemps.Max(s => s.Value) : 0;
        GpuTemp = gpuTemps.Any() ? gpuTemps.Max(s => s.Value) : 0;
    }

    private void UpdateFanData(IEnumerable<FanDevice> fans)
    {
        var fanList = fans.ToList();

        Fans.Clear();
        foreach (var fan in fanList)
        {
            Fans.Add(fan);
        }

        MaxFanRpm = fanList.Any() ? fanList.Max(f => f.CurrentRpm) : 0;
    }

    [RelayCommand]
    private async Task SetFanSpeedAsync(string fanIdKey)
    {
        // This would be called from the UI with the fan ID and percentage
        // Implemented in FansViewModel
    }

    [RelayCommand]
    private async Task SetProfileAsync(string profileId)
    {
        if (!IsConnected) return;

        try
        {
            var success = await _pipeClient.SetProfileAsync(profileId);
            if (success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting profile");
            SetError($"Error setting profile: {ex.Message}");
        }
    }
}
