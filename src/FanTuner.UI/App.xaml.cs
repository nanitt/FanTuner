using System.Windows;
using FanTuner.Core.IPC;
using FanTuner.Core.Logging;
using FanTuner.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FanTuner.UI;

/// <summary>
/// Application entry point with dependency injection setup
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services => ((App)Current)._serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Get logger and log startup
        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("FanTuner UI starting...");

        // Create and show main window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FanTuner", "logs");

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddFileLogger(logDirectory, "fantuner-ui", maxFileSizeMb: 5, maxFileCount: 3);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // IPC Client
        services.AddSingleton<PipeClient>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<FansViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        logger?.LogInformation("FanTuner UI shutting down...");

        // Dispose pipe client
        var pipeClient = _serviceProvider?.GetService<PipeClient>();
        pipeClient?.Dispose();

        _serviceProvider?.Dispose();

        base.OnExit(e);
    }
}
