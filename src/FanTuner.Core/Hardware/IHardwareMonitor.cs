using FanTuner.Core.Models;

namespace FanTuner.Core.Hardware;

/// <summary>
/// Event args for hardware changes
/// </summary>
public class HardwareChangedEventArgs : EventArgs
{
    public string Message { get; }
    public bool WasAdded { get; }

    public HardwareChangedEventArgs(string message, bool wasAdded)
    {
        Message = message;
        WasAdded = wasAdded;
    }
}

/// <summary>
/// Interface for reading hardware sensors
/// </summary>
public interface IHardwareMonitor : IDisposable
{
    /// <summary>
    /// Initialize the hardware monitor
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the monitor is initialized and ready
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Get all current sensor readings
    /// </summary>
    Task<IReadOnlyList<SensorReading>> GetSensorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sensors of a specific type
    /// </summary>
    Task<IReadOnlyList<SensorReading>> GetSensorsByTypeAsync(SensorType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific sensor reading
    /// </summary>
    Task<SensorReading?> GetSensorAsync(SensorId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh all hardware readings
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when hardware is added or removed
    /// </summary>
    event EventHandler<HardwareChangedEventArgs>? HardwareChanged;
}

/// <summary>
/// Interface for controlling fan speeds
/// </summary>
public interface IFanController : IDisposable
{
    /// <summary>
    /// Get all detected fan devices
    /// </summary>
    Task<IReadOnlyList<FanDevice>> GetFansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the control capability for a specific fan
    /// </summary>
    Task<FanControlCapability> GetCapabilityAsync(FanId fanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the speed of a fan (0-100%)
    /// </summary>
    /// <returns>True if successfully set, false if control not available</returns>
    Task<bool> SetSpeedAsync(FanId fanId, float percent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a fan to automatic (BIOS) control
    /// </summary>
    Task<bool> SetAutoAsync(FanId fanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set all controllable fans to auto mode
    /// </summary>
    Task SetAllAutoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Combined interface for hardware adapter implementations
/// </summary>
public interface IHardwareAdapter : IHardwareMonitor, IFanController
{
    /// <summary>
    /// Name of this adapter (e.g., "LibreHardwareMonitor", "Mock")
    /// </summary>
    string AdapterName { get; }

    /// <summary>
    /// Whether this adapter is available on the current system
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Any warnings or limitations of this adapter
    /// </summary>
    IReadOnlyList<string> Warnings { get; }
}
