using FanTuner.Core.Hardware;
using FanTuner.Core.IPC;
using FanTuner.Core.Logging;
using FanTuner.Core.Services;
using FanTuner.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
var logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "FanTuner", "logs");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFileLogger(logDirectory, "fantuner-service", maxFileSizeMb: 10, maxFileCount: 5);

// Configure services
builder.Services.AddSingleton<ConfigurationManager>();
builder.Services.AddSingleton<PipeServer>();

// Hardware adapter - use real adapter in production, mock for testing
var useMock = args.Contains("--mock");
if (useMock)
{
    builder.Services.AddSingleton<IHardwareAdapter, MockHardwareAdapter>();
    Console.WriteLine("Running with mock hardware adapter");
}
else
{
    builder.Services.AddSingleton<IHardwareAdapter, LibreHardwareMonitorAdapter>();
}

builder.Services.AddSingleton<IHardwareMonitor>(sp => sp.GetRequiredService<IHardwareAdapter>());
builder.Services.AddSingleton<IFanController>(sp => sp.GetRequiredService<IHardwareAdapter>());
builder.Services.AddSingleton<SafetyManager>();

// Add the main service worker
builder.Services.AddHostedService<FanTunerServiceWorker>();

// Configure as Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FanTuner";
});

var host = builder.Build();

// Log startup
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("FanTuner Service starting. Version 1.0.0");
logger.LogInformation("Log directory: {LogDir}", logDirectory);

await host.RunAsync();
