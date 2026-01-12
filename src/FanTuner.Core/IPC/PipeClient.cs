using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.IPC;

/// <summary>
/// Event args for connection state changes
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; }
    public string? Error { get; }

    public ConnectionStateChangedEventArgs(bool isConnected, string? error = null)
    {
        IsConnected = isConnected;
        Error = error;
    }
}

/// <summary>
/// Named pipe client for UI communication with service
/// </summary>
public class PipeClient : IDisposable
{
    private readonly ILogger<PipeClient> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PipeMessage>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _receiveCts;
    private bool _isConnected;
    private bool _disposed;

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<SensorUpdateNotification>? SensorUpdateReceived;

    public bool IsConnected => _isConnected && _pipe?.IsConnected == true;

    public PipeClient(ILogger<PipeClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connect to the service
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return true;

        try
        {
            _pipe = new NamedPipeClientStream(
                ".",
                PipeConstants.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PipeConstants.ConnectionTimeout);

            await _pipe.ConnectAsync(cts.Token);

            _isConnected = true;
            _receiveCts = new CancellationTokenSource();

            // Start receive loop
            _ = ReceiveLoopAsync(_receiveCts.Token);

            _logger.LogInformation("Connected to FanTuner service");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(true));

            return true;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Connection to service timed out");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "Connection timed out"));
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to service");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Disconnect from the service
    /// </summary>
    public void Disconnect()
    {
        if (!_isConnected) return;

        _isConnected = false;
        _receiveCts?.Cancel();

        try
        {
            _pipe?.Close();
            _pipe?.Dispose();
            _pipe = null;
        }
        catch { }

        // Cancel all pending requests
        foreach (var pending in _pendingRequests.Values)
        {
            pending.TrySetCanceled();
        }
        _pendingRequests.Clear();

        _logger.LogInformation("Disconnected from FanTuner service");
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false));
    }

    /// <summary>
    /// Send a request and wait for response
    /// </summary>
    public async Task<TResponse?> SendRequestAsync<TResponse>(PipeMessage request, CancellationToken cancellationToken = default)
        where TResponse : PipeMessage
    {
        if (!IsConnected)
        {
            if (!await ConnectAsync(cancellationToken))
            {
                return null;
            }
        }

        var tcs = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.RequestId] = tcs;

        try
        {
            await SendMessageAsync(request, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PipeConstants.ReadTimeout);

            using var _ = cts.Token.Register(() => tcs.TrySetCanceled());

            var response = await tcs.Task;
            return response as TResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request failed: {RequestType}", request.GetType().Name);
            throw;
        }
        finally
        {
            _pendingRequests.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>
    /// Send a message without waiting for response
    /// </summary>
    public async Task SendMessageAsync(PipeMessage message, CancellationToken cancellationToken = default)
    {
        if (_pipe == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to service");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var json = message.Serialize();
            var bytes = Encoding.UTF8.GetBytes(json);
            var lengthBytes = BitConverter.GetBytes(bytes.Length);

            await _pipe.WriteAsync(lengthBytes, cancellationToken);
            await _pipe.WriteAsync(bytes, cancellationToken);
            await _pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Get service status
    /// </summary>
    public Task<StatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<StatusResponse>(new GetStatusRequest(), cancellationToken);
    }

    /// <summary>
    /// Get sensor readings
    /// </summary>
    public Task<SensorsResponse?> GetSensorsAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<SensorsResponse>(new GetSensorsRequest(), cancellationToken);
    }

    /// <summary>
    /// Get fan devices
    /// </summary>
    public Task<FansResponse?> GetFansAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<FansResponse>(new GetFansRequest(), cancellationToken);
    }

    /// <summary>
    /// Get configuration
    /// </summary>
    public Task<ConfigResponse?> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync<ConfigResponse>(new GetConfigRequest(), cancellationToken);
    }

    /// <summary>
    /// Set fan speed
    /// </summary>
    public async Task<bool> SetFanSpeedAsync(string fanIdKey, float percent, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync<AckResponse>(
            new SetFanSpeedRequest { FanIdKey = fanIdKey, Percent = percent },
            cancellationToken);
        return response?.Success == true;
    }

    /// <summary>
    /// Set active profile
    /// </summary>
    public async Task<bool> SetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync<AckResponse>(
            new SetProfileRequest { ProfileId = profileId },
            cancellationToken);
        return response?.Success == true;
    }

    /// <summary>
    /// Subscribe to sensor updates
    /// </summary>
    public async Task<bool> SubscribeSensorsAsync(int intervalMs = 1000, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync<AckResponse>(
            new SubscribeSensorsRequest { IntervalMs = intervalMs },
            cancellationToken);
        return response?.Success == true;
    }

    /// <summary>
    /// Unsubscribe from sensor updates
    /// </summary>
    public async Task UnsubscribeSensorsAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync<AckResponse>(new UnsubscribeSensorsRequest(), cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var message = await ReceiveMessageAsync(cancellationToken);
                if (message == null)
                {
                    _logger.LogDebug("Connection closed by server");
                    break;
                }

                HandleReceivedMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop");
        }
        finally
        {
            if (_isConnected)
            {
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(false, "Connection lost"));
            }
        }
    }

    private async Task<PipeMessage?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        if (_pipe == null) return null;

        // Read message length
        var lengthBytes = new byte[4];
        var bytesRead = await _pipe.ReadAsync(lengthBytes, cancellationToken);
        if (bytesRead == 0) return null;

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
        if (messageLength <= 0 || messageLength > PipeConstants.MaxMessageSize)
        {
            _logger.LogWarning("Invalid message length: {Length}", messageLength);
            return null;
        }

        // Read message body
        var buffer = new byte[messageLength];
        var totalRead = 0;
        while (totalRead < messageLength)
        {
            bytesRead = await _pipe.ReadAsync(
                buffer.AsMemory(totalRead, messageLength - totalRead),
                cancellationToken);

            if (bytesRead == 0) return null;
            totalRead += bytesRead;
        }

        var json = Encoding.UTF8.GetString(buffer);
        return PipeMessage.Deserialize(json);
    }

    private void HandleReceivedMessage(PipeMessage message)
    {
        // Check if this is a response to a pending request
        if (message is AckResponse ack && ack.OriginalRequestId != null)
        {
            if (_pendingRequests.TryRemove(ack.OriginalRequestId, out var tcs))
            {
                tcs.TrySetResult(message);
                return;
            }
        }
        else if (message is ErrorResponse error && error.OriginalRequestId != null)
        {
            if (_pendingRequests.TryRemove(error.OriginalRequestId, out var tcs))
            {
                tcs.TrySetResult(message);
                return;
            }
        }
        else if (_pendingRequests.TryRemove(message.RequestId, out var tcs))
        {
            tcs.TrySetResult(message);
            return;
        }

        // Handle push notifications
        if (message is SensorUpdateNotification update)
        {
            SensorUpdateReceived?.Invoke(this, update);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        _receiveCts?.Dispose();
        _writeLock.Dispose();
    }
}
