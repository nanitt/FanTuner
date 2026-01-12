using System.Diagnostics;
using FanTuner.Core.Hardware;
using FanTuner.Core.IPC;
using FanTuner.Core.Models;
using FanTuner.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FanTuner.Service;

/// <summary>
/// Main service worker that handles hardware monitoring and fan control
/// </summary>
public class FanTunerServiceWorker : BackgroundService
{
    private readonly ILogger<FanTunerServiceWorker> _logger;
    private readonly IHardwareAdapter _hardware;
    private readonly ConfigurationManager _configManager;
    private readonly SafetyManager _safetyManager;
    private readonly PipeServer _pipeServer;
    private readonly Stopwatch _uptimeStopwatch = new();
    private readonly Dictionary<string, float> _lastFanOutputs = new();

    private AppConfiguration _config = AppConfiguration.CreateDefault();
    private List<SensorReading> _lastSensors = new();
    private List<FanDevice> _lastFans = new();

    public FanTunerServiceWorker(
        ILogger<FanTunerServiceWorker> logger,
        IHardwareAdapter hardware,
        ConfigurationManager configManager,
        SafetyManager safetyManager,
        PipeServer pipeServer)
    {
        _logger = logger;
        _hardware = hardware;
        _configManager = configManager;
        _safetyManager = safetyManager;
        _pipeServer = pipeServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FanTuner Service starting...");
        _uptimeStopwatch.Start();

        try
        {
            // Load configuration
            _config = await _configManager.LoadAsync(stoppingToken);
            _safetyManager.UpdateThresholds(_config);

            // Initialize hardware
            await _hardware.InitializeAsync(stoppingToken);

            // Log warnings
            foreach (var warning in _hardware.Warnings)
            {
                _logger.LogWarning("Hardware warning: {Warning}", warning);
            }

            // Setup pipe server
            _pipeServer.MessageReceived += OnMessageReceived;
            _pipeServer.ClientConnected += (s, e) =>
                _logger.LogDebug("Client connected: {ClientId}", e.ClientId);
            _pipeServer.ClientDisconnected += (s, e) =>
                _logger.LogDebug("Client disconnected: {ClientId}", e.ClientId);
            _pipeServer.Start();

            // Setup safety alerts
            _safetyManager.SafetyAlert += (s, e) =>
                _logger.LogWarning("Safety alert [{Level}]: {Message}", e.Level, e.Message);

            // Main loop
            await RunMainLoopAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in service");
            throw;
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    private async Task RunMainLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Entering main loop with poll interval: {Interval}ms", _config.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var loopStart = Stopwatch.GetTimestamp();

                // Refresh hardware readings
                await _hardware.RefreshAsync(stoppingToken);

                // Get current readings
                _lastSensors = (await _hardware.GetSensorsAsync(stoppingToken)).ToList();
                _lastFans = (await _hardware.GetFansAsync(stoppingToken)).ToList();

                // Check safety conditions
                var isEmergency = await _safetyManager.CheckSensorsAsync(_lastSensors, stoppingToken);

                if (!isEmergency)
                {
                    // Apply fan curves
                    await ApplyFanControlAsync(stoppingToken);
                }

                // Broadcast to subscribed clients
                await BroadcastSensorUpdateAsync(isEmergency, stoppingToken);

                // Calculate time to sleep
                var elapsed = Stopwatch.GetElapsedTime(loopStart);
                var sleepTime = TimeSpan.FromMilliseconds(_config.PollIntervalMs) - elapsed;

                if (sleepTime > TimeSpan.Zero)
                {
                    await Task.Delay(sleepTime, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main loop");
                await _safetyManager.RecordFailureAsync(ex.Message, stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ApplyFanControlAsync(CancellationToken stoppingToken)
    {
        var profile = _config.GetActiveProfile();

        foreach (var fan in _lastFans.Where(f => f.CanControl))
        {
            try
            {
                var assignment = profile.GetOrCreateAssignment(fan.Id.UniqueKey);

                float? targetPercent = assignment.Mode switch
                {
                    FanControlMode.Auto => null, // Let BIOS handle
                    FanControlMode.Manual => assignment.ManualPercent,
                    FanControlMode.Curve => CalculateCurveOutput(assignment, fan),
                    _ => null
                };

                if (targetPercent.HasValue)
                {
                    // Enforce minimum
                    targetPercent = _safetyManager.EnforceMinimum(targetPercent.Value);

                    // Apply if changed
                    var lastKey = fan.Id.UniqueKey;
                    _lastFanOutputs.TryGetValue(lastKey, out var lastOutput);

                    if (Math.Abs(targetPercent.Value - lastOutput) > 0.5f)
                    {
                        await _hardware.SetSpeedAsync(fan.Id, targetPercent.Value, stoppingToken);
                        _lastFanOutputs[lastKey] = targetPercent.Value;
                        assignment.LastAppliedPercent = targetPercent.Value;
                    }
                }
                else if (assignment.Mode == FanControlMode.Auto)
                {
                    // Ensure fan is in auto mode
                    await _hardware.SetAutoAsync(fan.Id, stoppingToken);
                    _lastFanOutputs.Remove(fan.Id.UniqueKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply control to fan: {FanName}", fan.DisplayName);
            }
        }
    }

    private float? CalculateCurveOutput(FanAssignment assignment, FanDevice fan)
    {
        if (string.IsNullOrEmpty(assignment.CurveId))
            return null;

        var curve = _config.GetCurve(assignment.CurveId);
        if (curve == null)
            return null;

        // Get temperature from curve's source sensor
        float? temperature = null;

        if (curve.SourceSensor != null)
        {
            var sensor = _lastSensors.FirstOrDefault(s =>
                s.Id.UniqueKey == curve.SourceSensor.UniqueKey);
            temperature = sensor?.Value;
        }

        // Fallback: use first CPU temp
        if (!temperature.HasValue)
        {
            var cpuTemp = _lastSensors.FirstOrDefault(s =>
                s.HardwareType == HardwareType.Cpu &&
                s.Id.Type == SensorType.Temperature);
            temperature = cpuTemp?.Value;
        }

        if (!temperature.HasValue)
            return null;

        _lastFanOutputs.TryGetValue(fan.Id.UniqueKey, out var lastOutput);

        return CurveEngine.Interpolate(curve, temperature.Value, lastOutput > 0 ? lastOutput : null);
    }

    private async Task BroadcastSensorUpdateAsync(bool isEmergency, CancellationToken stoppingToken)
    {
        if (_pipeServer.ConnectedClientCount == 0)
            return;

        var notification = new SensorUpdateNotification
        {
            Sensors = _lastSensors,
            Fans = _lastFans,
            EmergencyModeActive = isEmergency
        };

        await _pipeServer.BroadcastAsync(notification, stoppingToken);
    }

    private async void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            var response = await HandleMessageAsync(e.Message);
            if (response != null)
            {
                await _pipeServer.SendToClientAsync(e.ClientId, response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from client {ClientId}", e.ClientId);

            var errorResponse = new ErrorResponse
            {
                Error = "Internal error",
                Details = ex.Message,
                OriginalRequestId = e.Message.RequestId
            };
            await _pipeServer.SendToClientAsync(e.ClientId, errorResponse);
        }
    }

    private async Task<PipeMessage?> HandleMessageAsync(PipeMessage message)
    {
        return message switch
        {
            GetStatusRequest => HandleGetStatus(),
            GetSensorsRequest => HandleGetSensors(),
            GetFansRequest => HandleGetFans(),
            GetConfigRequest => HandleGetConfig(),
            SetConfigRequest req => await HandleSetConfigAsync(req),
            SetFanSpeedRequest req => await HandleSetFanSpeedAsync(req),
            SetProfileRequest req => await HandleSetProfileAsync(req),
            SubscribeSensorsRequest => new AckResponse { Success = true, OriginalRequestId = message.RequestId },
            UnsubscribeSensorsRequest => new AckResponse { Success = true, OriginalRequestId = message.RequestId },
            _ => new ErrorResponse { Error = "Unknown message type", OriginalRequestId = message.RequestId }
        };
    }

    private StatusResponse HandleGetStatus()
    {
        var status = _safetyManager.GetStatus();

        return new StatusResponse
        {
            IsRunning = true,
            Version = "1.0.0",
            UptimeSeconds = (int)_uptimeStopwatch.Elapsed.TotalSeconds,
            EmergencyModeActive = status.IsEmergencyMode,
            EmergencyReason = status.EmergencyReason,
            ActiveProfileId = _config.ActiveProfileId,
            ActiveProfileName = _config.GetActiveProfile().Name,
            Warnings = _hardware.Warnings.Concat(status.ActiveWarnings).ToList(),
            ConnectedClients = _pipeServer.ConnectedClientCount
        };
    }

    private SensorsResponse HandleGetSensors()
    {
        return new SensorsResponse { Sensors = _lastSensors };
    }

    private FansResponse HandleGetFans()
    {
        return new FansResponse { Fans = _lastFans };
    }

    private ConfigResponse HandleGetConfig()
    {
        return new ConfigResponse { Config = _config };
    }

    private async Task<AckResponse> HandleSetConfigAsync(SetConfigRequest request)
    {
        if (request.Config == null)
        {
            return new AckResponse
            {
                Success = false,
                Message = "Config is null",
                OriginalRequestId = request.RequestId
            };
        }

        await _configManager.SaveAsync(request.Config);
        _config = request.Config;
        _safetyManager.UpdateThresholds(_config);

        return new AckResponse
        {
            Success = true,
            Message = "Configuration updated",
            OriginalRequestId = request.RequestId
        };
    }

    private async Task<AckResponse> HandleSetFanSpeedAsync(SetFanSpeedRequest request)
    {
        var fan = _lastFans.FirstOrDefault(f => f.Id.UniqueKey == request.FanIdKey);
        if (fan == null)
        {
            return new AckResponse
            {
                Success = false,
                Message = "Fan not found",
                OriginalRequestId = request.RequestId
            };
        }

        if (!fan.CanControl)
        {
            return new AckResponse
            {
                Success = false,
                Message = "Fan does not support control",
                OriginalRequestId = request.RequestId
            };
        }

        var success = await _hardware.SetSpeedAsync(fan.Id, request.Percent);

        return new AckResponse
        {
            Success = success,
            Message = success ? $"Fan set to {request.Percent}%" : "Failed to set fan speed",
            OriginalRequestId = request.RequestId
        };
    }

    private async Task<AckResponse> HandleSetProfileAsync(SetProfileRequest request)
    {
        if (!_config.Profiles.Any(p => p.Id == request.ProfileId))
        {
            return new AckResponse
            {
                Success = false,
                Message = "Profile not found",
                OriginalRequestId = request.RequestId
            };
        }

        await _configManager.SetActiveProfileAsync(request.ProfileId);
        _config.ActiveProfileId = request.ProfileId;

        return new AckResponse
        {
            Success = true,
            Message = $"Profile changed to {_config.GetActiveProfile().Name}",
            OriginalRequestId = request.RequestId
        };
    }

    private async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down...");

        try
        {
            // Return all fans to auto
            _logger.LogInformation("Returning fans to automatic control");
            await _hardware.SetAllAutoAsync();

            // Stop pipe server
            _pipeServer.Stop();

            // Save config
            await _configManager.SaveAsync(_config);

            // Dispose hardware
            _hardware.Dispose();

            _logger.LogInformation("Shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown");
        }
    }
}
