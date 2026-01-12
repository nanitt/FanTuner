namespace FanTuner.Core.Models;

/// <summary>
/// Type of sensor reading
/// </summary>
public enum SensorType
{
    Temperature,
    Fan,
    Load,
    Voltage,
    Clock,
    Power,
    Data,
    SmallData,
    Throughput,
    Control,
    Level,
    Factor,
    Frequency,
    TimeSpan,
    Energy,
    Noise
}

/// <summary>
/// Type of hardware component
/// </summary>
public enum HardwareType
{
    Cpu,
    GpuNvidia,
    GpuAmd,
    GpuIntel,
    Motherboard,
    Ram,
    Storage,
    Network,
    Cooler,
    EmbeddedController,
    Psu,
    Battery,
    Unknown
}

/// <summary>
/// Fan control capability
/// </summary>
public enum FanControlCapability
{
    /// <summary>Can read RPM and set speed percentage</summary>
    FullControl,
    /// <summary>Can only read RPM, no speed control</summary>
    MonitorOnly,
    /// <summary>Capability not yet determined</summary>
    Unknown,
    /// <summary>Fan control failed or is unavailable</summary>
    Unavailable
}

/// <summary>
/// Fan control mode
/// </summary>
public enum FanControlMode
{
    /// <summary>BIOS/hardware automatic control</summary>
    Auto,
    /// <summary>Fixed percentage set by user</summary>
    Manual,
    /// <summary>Temperature-based curve</summary>
    Curve
}
