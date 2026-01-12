using System.Windows;
using FanTuner.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FanTuner.UI;

/// <summary>
/// Main window code-behind
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var config = _viewModel.Configuration;

        if (config?.MinimizeToTray == true)
        {
            // Would minimize to tray here
            // For now, just close
        }

        _viewModel.DisconnectCommand.Execute(null);
    }
}
