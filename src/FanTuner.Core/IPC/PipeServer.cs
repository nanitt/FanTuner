using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FanTuner.Core.IPC;

/// <summary>
/// Event args for client connection
/// </summary>
public class ClientConnectedEventArgs : EventArgs
{
    public string ClientId { get; }
    public ClientConnectedEventArgs(string clientId) => ClientId = clientId;
}

/// <summary>
/// Event args for received message
/// </summary>
public class MessageReceivedEventArgs : EventArgs
{
    public string ClientId { get; }
    public PipeMessage Message { get; }
    public MessageReceivedEventArgs(string clientId, PipeMessage message)
    {
        ClientId = clientId;
        Message = message;
    }
}

/// <summary>
/// Named pipe server for service communication
/// </summary>
public class PipeServer : IDisposable
{
    private readonly ILogger<PipeServer> _logger;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxClients;
    private bool _isRunning;
    private bool _disposed;

    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientConnectedEventArgs>? ClientDisconnected;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public int ConnectedClientCount => _clients.Count;

    public PipeServer(ILogger<PipeServer> logger, int maxClients = 10)
    {
        _logger = logger;
        _maxClients = maxClients;
    }

    /// <summary>
    /// Start the pipe server
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _logger.LogInformation("Starting pipe server on pipe: {PipeName}", PipeConstants.PipeName);

        // Start multiple listener tasks
        for (int i = 0; i < Math.Min(4, _maxClients); i++)
        {
            _ = AcceptClientsAsync(_cts.Token);
        }
    }

    /// <summary>
    /// Stop the pipe server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _logger.LogInformation("Stopping pipe server");
        _cts.Cancel();

        // Disconnect all clients
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    /// <summary>
    /// Send a message to a specific client
    /// </summary>
    public async Task SendToClientAsync(string clientId, PipeMessage message, CancellationToken cancellationToken = default)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            await client.SendAsync(message, cancellationToken);
        }
    }

    /// <summary>
    /// Send a message to all connected clients
    /// </summary>
    public async Task BroadcastAsync(PipeMessage message, CancellationToken cancellationToken = default)
    {
        var tasks = _clients.Values
            .Where(c => c.IsSubscribed)
            .Select(c => c.SendAsync(message, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                // Create pipe with security that allows all users to connect
                var pipeSecurity = CreatePipeSecurity();

                var pipeServer = NamedPipeServerStreamAcl.Create(
                    PipeConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    PipeConstants.MaxMessageSize,
                    PipeConstants.MaxMessageSize,
                    pipeSecurity);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                if (_clients.Count >= _maxClients)
                {
                    _logger.LogWarning("Max clients reached, rejecting connection");
                    pipeServer.Close();
                    continue;
                }

                var clientId = Guid.NewGuid().ToString();
                var client = new ClientConnection(clientId, pipeServer, _logger);

                if (_clients.TryAdd(clientId, client))
                {
                    _logger.LogDebug("Client connected: {ClientId}", clientId);
                    ClientConnected?.Invoke(this, new ClientConnectedEventArgs(clientId));

                    // Start receiving messages from this client
                    _ = HandleClientAsync(client, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(ClientConnection client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && client.IsConnected)
            {
                var message = await client.ReceiveAsync(cancellationToken);
                if (message == null)
                {
                    break;
                }

                // Handle subscription requests
                if (message is SubscribeSensorsRequest)
                {
                    client.IsSubscribed = true;
                }
                else if (message is UnsubscribeSensorsRequest)
                {
                    client.IsSubscribed = false;
                }

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(client.Id, message));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Client disconnected: {ClientId}", client.Id);
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            ClientDisconnected?.Invoke(this, new ClientConnectedEventArgs(client.Id));
            client.Dispose();
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        // Allow everyone to connect (read/write)
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        // Allow system full control
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return security;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts.Dispose();
    }
}

/// <summary>
/// Represents a connected client
/// </summary>
internal class ClientConnection : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public string Id { get; }
    public bool IsConnected => _pipe.IsConnected;
    public bool IsSubscribed { get; set; }

    public ClientConnection(string id, NamedPipeServerStream pipe, ILogger logger)
    {
        Id = id;
        _pipe = pipe;
        _logger = logger;
    }

    public async Task SendAsync(PipeMessage message, CancellationToken cancellationToken)
    {
        if (!IsConnected || _disposed) return;

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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send message to client {ClientId}", Id);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PipeMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected || _disposed) return null;

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to receive message from client {ClientId}", Id);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _pipe.Close();
            _pipe.Dispose();
        }
        catch { }

        _writeLock.Dispose();
    }
}
