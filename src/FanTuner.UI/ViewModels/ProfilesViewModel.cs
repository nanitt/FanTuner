using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FanTuner.Core.IPC;
using FanTuner.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanTuner.UI.ViewModels;

/// <summary>
/// Profiles view ViewModel
/// </summary>
public partial class ProfilesViewModel : ViewModelBase
{
    private readonly ILogger<ProfilesViewModel> _logger;
    private readonly MainViewModel _mainViewModel;
    private readonly PipeClient _pipeClient;

    [ObservableProperty]
    private ObservableCollection<FanProfile> _profiles = new();

    [ObservableProperty]
    private FanProfile? _selectedProfile;

    [ObservableProperty]
    private ObservableCollection<FanCurve> _curves = new();

    [ObservableProperty]
    private FanCurve? _selectedCurve;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newCurveName = string.Empty;

    public bool IsConnected => _mainViewModel.IsConnected;
    public string ActiveProfileId => _mainViewModel.Configuration?.ActiveProfileId ?? string.Empty;

    public ProfilesViewModel(
        ILogger<ProfilesViewModel> logger,
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
                RefreshData();
            }
        };
    }

    public void Initialize()
    {
        RefreshData();
    }

    private void RefreshData()
    {
        if (_mainViewModel.Configuration == null) return;

        Profiles.Clear();
        foreach (var profile in _mainViewModel.Configuration.Profiles)
        {
            Profiles.Add(profile);
        }

        Curves.Clear();
        foreach (var curve in _mainViewModel.Configuration.Curves)
        {
            Curves.Add(curve);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();
        SelectedCurve = Curves.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ActivateProfileAsync(FanProfile? profile)
    {
        if (profile == null || !IsConnected) return;

        IsLoading = true;
        try
        {
            var success = await _pipeClient.SetProfileAsync(profile.Id);
            if (success)
            {
                await _mainViewModel.RefreshCommand.ExecuteAsync(null);
            }
            else
            {
                SetError("Failed to activate profile");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating profile");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName) || _mainViewModel.Configuration == null) return;

        IsLoading = true;
        try
        {
            var newProfile = new FanProfile
            {
                Name = NewProfileName.Trim(),
                Description = "Custom profile"
            };

            _mainViewModel.Configuration.Profiles.Add(newProfile);

            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success == true)
            {
                NewProfileName = string.Empty;
                RefreshData();
            }
            else
            {
                _mainViewModel.Configuration.Profiles.Remove(newProfile);
                SetError("Failed to create profile");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(FanProfile? profile)
    {
        if (profile == null || profile.IsDefault || _mainViewModel.Configuration == null) return;

        IsLoading = true;
        try
        {
            _mainViewModel.Configuration.Profiles.Remove(profile);

            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success == true)
            {
                RefreshData();
            }
            else
            {
                _mainViewModel.Configuration.Profiles.Add(profile);
                SetError("Failed to delete profile");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateCurveAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCurveName) || _mainViewModel.Configuration == null) return;

        IsLoading = true;
        try
        {
            var newCurve = FanCurve.CreateDefault(NewCurveName.Trim());

            _mainViewModel.Configuration.Curves.Add(newCurve);

            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success == true)
            {
                NewCurveName = string.Empty;
                RefreshData();
                SelectedCurve = newCurve;
            }
            else
            {
                _mainViewModel.Configuration.Curves.Remove(newCurve);
                SetError("Failed to create curve");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating curve");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCurveAsync(FanCurve? curve)
    {
        if (curve == null || _mainViewModel.Configuration == null) return;

        // Don't allow deleting if it's the last curve
        if (_mainViewModel.Configuration.Curves.Count <= 1)
        {
            SetError("Cannot delete the last curve");
            return;
        }

        IsLoading = true;
        try
        {
            _mainViewModel.Configuration.Curves.Remove(curve);

            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success == true)
            {
                RefreshData();
            }
            else
            {
                _mainViewModel.Configuration.Curves.Add(curve);
                SetError("Failed to delete curve");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting curve");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveCurveAsync()
    {
        if (SelectedCurve == null || _mainViewModel.Configuration == null) return;

        IsLoading = true;
        try
        {
            var response = await _pipeClient.SendRequestAsync<AckResponse>(
                new SetConfigRequest { Config = _mainViewModel.Configuration });

            if (response?.Success != true)
            {
                SetError("Failed to save curve");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving curve");
            SetError($"Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
