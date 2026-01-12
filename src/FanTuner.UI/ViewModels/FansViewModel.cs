using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FanTuner.Core.IPC;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.UI.ViewModels;

/// <summary>
/// ViewModel for a single fan control item
/// </summary>
public partial class FanControlItem : ObservableObject
{
    private readonly PipeClient _pipeClient;

    [ObservableProperty]
    private FanDevice _device;

    [ObservableProperty]
    private FanControlMode _mode = FanControlMode.Auto;

    [ObservableProperty]
    private float _manualPercent = 50f;

    [ObservableProperty]
    private string? _selectedCurveId;

    [ObservableProperty]
    private bool _isBusy;

    public FanControlItem(FanDevice device, PipeClient pipeClient)
    {
        _device = device;
        _pipeClient = pipeClient;
    }

    [RelayCommand]
    private async Task SetManualSpeedAsync()
    {
        if (!Device.CanControl) return;

        IsBusy = true;
        try
        {
            await _pipeClient.SetFanSpeedAsync(Device.Id.UniqueKey, ManualPercent);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Fans view ViewModel
/// </summary>
public partial class FansViewModel : ViewModelBase
{
    private readonly ILogger<FansViewModel> _logger;
    private readonly MainViewModel _mainViewModel;
    private readonly PipeClient _pipeClient;

    [ObservableProperty]
    private ObservableCollection<FanControlItem> _fanItems = new();

    [ObservableProperty]
    private FanControlItem? _selectedFan;

    [ObservableProperty]
    private ObservableCollection<FanCurve> _availableCurves = new();

    public bool IsConnected => _mainViewModel.IsConnected;
    public ObservableCollection<FanDevice> Fans => _mainViewModel.Fans;
    public AppConfiguration? Configuration => _mainViewModel.Configuration;

    public FansViewModel(
        ILogger<FansViewModel> logger,
        MainViewModel mainViewModel,
        PipeClient pipeClient)
    {
        _logger = logger;
        _mainViewModel = mainViewModel;
        _pipeClient = pipeClient;

        // Subscribe to fan changes
        _mainViewModel.Fans.CollectionChanged += (s, e) => RefreshFanItems();
        _mainViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Configuration))
            {
                RefreshCurves();
            }
        };
    }

    public void Initialize()
    {
        RefreshFanItems();
        RefreshCurves();
    }

    private void RefreshFanItems()
    {
        FanItems.Clear();
        foreach (var fan in _mainViewModel.Fans)
        {
            var item = new FanControlItem(fan, _pipeClient);

            // Load current assignment from profile
            if (_mainViewModel.Configuration != null)
            {
                var profile = _mainViewModel.Configuration.GetActiveProfile();
                var assignment = profile.FanAssignments.GetValueOrDefault(fan.Id.UniqueKey);

                if (assignment != null)
                {
                    item.Mode = assignment.Mode;
                    item.ManualPercent = assignment.ManualPercent ?? 50f;
                    item.SelectedCurveId = assignment.CurveId;
                }
            }

            FanItems.Add(item);
        }
    }

    private void RefreshCurves()
    {
        AvailableCurves.Clear();

        if (_mainViewModel.Configuration?.Curves != null)
        {
            foreach (var curve in _mainViewModel.Configuration.Curves)
            {
                AvailableCurves.Add(curve);
            }
        }
    }

    [RelayCommand]
    private async Task SetAllAutoAsync()
    {
        foreach (var item in FanItems.Where(f => f.Device.CanControl))
        {
            item.Mode = FanControlMode.Auto;
        }

        // Save to configuration
        await SaveFanAssignmentsAsync();
    }

    [RelayCommand]
    private async Task SaveFanAssignmentsAsync()
    {
        if (_mainViewModel.Configuration == null || !IsConnected) return;

        IsLoading = true;
        try
        {
            var profile = _mainViewModel.Configuration.GetActiveProfile();

            foreach (var item in FanItems)
            {
                var assignment = profile.GetOrCreateAssignment(item.Device.Id.UniqueKey);
                assignment.Mode = item.Mode;
                assignment.ManualPercent = item.Mode == FanControlMode.Manual ? item.ManualPercent : null;
                assignment.CurveId = item.Mode == FanControlMode.Curve ? item.SelectedCurveId : null;
            }

            // Send updated config to service
            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success != true)
            {
                SetError("Failed to save fan assignments");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving fan assignments");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
