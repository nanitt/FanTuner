using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FanTuner.Core.Models;

namespace FanTuner.UI.ViewModels;

/// <summary>
/// Dashboard view ViewModel
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly MainViewModel _mainViewModel;

    public DashboardViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public bool IsConnected => _mainViewModel.IsConnected;
    public string ConnectionStatus => _mainViewModel.ConnectionStatus;
    public bool IsEmergencyMode => _mainViewModel.IsEmergencyMode;
    public string? EmergencyReason => _mainViewModel.EmergencyReason;
    public string ActiveProfileName => _mainViewModel.ActiveProfileName;
    public float CpuTemp => _mainViewModel.CpuTemp;
    public float GpuTemp => _mainViewModel.GpuTemp;
    public float MaxFanRpm => _mainViewModel.MaxFanRpm;
    public ObservableCollection<SensorReading> TemperatureSensors => _mainViewModel.TemperatureSensors;
    public ObservableCollection<FanDevice> Fans => _mainViewModel.Fans;
    public ObservableCollection<string> Warnings => _mainViewModel.Warnings;
}
