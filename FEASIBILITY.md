# PC Fan Tuner - Feasibility Analysis

## Hardware Access Reality Check

### LibreHardwareMonitor Capabilities

**What WORKS reliably:**
- CPU temperature (all modern Intel/AMD CPUs)
- GPU temperature (NVIDIA, AMD, Intel Arc)
- Motherboard/chipset temperatures (most boards)
- Fan RPM readings (most motherboards)
- Drive temperatures (NVMe, SATA)
- RAM temperature (boards with sensors)

**What works SOMETIMES (hardware-dependent):**
- Fan speed control via SuperIO chips (IT87xx, NCT67xx, Fintek F718xx, Nuvoton)
- GPU fan control (NVIDIA via NVAPI, AMD via ADL)
- Some AIO liquid cooler pumps

**What RARELY works:**
- Laptop fan control (usually locked by BIOS/EC)
- Proprietary fan hubs (Corsair iCUE, NZXT CAM, etc.)
- Some newer motherboards with locked EC

### Control Methods by Hardware

| Hardware Type | Read Sensors | Control Fans | Notes |
|--------------|--------------|--------------|-------|
| Intel CPUs | ✅ Yes | N/A | Temp only |
| AMD CPUs | ✅ Yes | N/A | Temp only |
| NVIDIA GPUs | ✅ Yes | ⚠️ Partial | Requires unsigned driver or NVAPI |
| AMD GPUs | ✅ Yes | ⚠️ Partial | Via ADL SDK |
| Motherboard fans | ✅ Yes | ⚠️ Varies | Depends on SuperIO chip |
| AIO Coolers | ⚠️ Some | ⚠️ Some | HID-based ones may work |
| Laptops | ✅ Yes | ❌ Rarely | EC locked |

### Our Implementation Strategy

**Tier 1 - Guaranteed:**
- All sensor monitoring (temps, RPMs, utilization)
- Display and logging of all readings
- Profile management and curve definitions

**Tier 2 - Best Effort:**
- SuperIO-based motherboard fan control
- GPU fan control via manufacturer APIs
- Report "control supported" vs "monitor only" per device

**Tier 3 - Not Supported (v1.0):**
- USB fan controllers (Corsair, NZXT, etc.)
- RGB/fan combo devices
- Direct EC manipulation for laptops

## Technical Decisions

### IPC: Named Pipes vs Local HTTP

**Chosen: Named Pipes**

Rationale:
- Lower latency (<1ms vs 5-10ms for HTTP)
- No port conflicts
- Built-in Windows security (ACLs)
- Smaller memory footprint
- Binary serialization possible
- No firewall issues

Named pipe: `\\.\pipe\FanTuner`

### Data Serialization

**Chosen: System.Text.Json**

- Native .NET 8 support
- AOT-compatible
- Fast enough for our use case
- Human-readable config files

### Admin Elevation Model

1. **Service**: Always runs as LocalSystem (full hardware access)
2. **UI**: Runs as standard user normally
3. **Elevation needed for**:
   - Installing/uninstalling service
   - Starting/stopping service
   - Modifying config in ProgramData (service does this on behalf of UI)

### Safety Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    SAFETY LAYERS                        │
├─────────────────────────────────────────────────────────┤
│ Layer 1: Watchdog Timer                                 │
│   - Service pings hardware every 500ms                  │
│   - If 3 consecutive failures → revert to Auto          │
├─────────────────────────────────────────────────────────┤
│ Layer 2: Temperature Emergency                          │
│   - CPU > threshold (default 95°C) → 100% fans          │
│   - GPU > threshold (default 90°C) → 100% fans          │
│   - Hysteresis: 5°C before returning to curve           │
├─────────────────────────────────────────────────────────┤
│ Layer 3: Minimum Fan Speed                              │
│   - User-configurable minimum (default 20%)             │
│   - Prevents fan stall on low-quality fans              │
├─────────────────────────────────────────────────────────┤
│ Layer 4: Graceful Degradation                           │
│   - Sensor disappears → use last known + warning        │
│   - Control fails → mark as monitor-only, continue      │
│   - Service crash → SCM restarts, reverts to Auto       │
└─────────────────────────────────────────────────────────┘
```

## Threat Model

### Risks and Mitigations

| Threat | Impact | Mitigation |
|--------|--------|------------|
| Malicious config modification | Overheating | Config in ProgramData, ACL'd to admins only |
| Service DoS via pipe spam | Service crash | Rate limiting, message size limits |
| Privilege escalation via service | System compromise | Validate all IPC messages, don't execute arbitrary commands |
| Race condition in fan control | Brief overheating | Atomic config reads, watchdog timer |
| Conflicting software | Unpredictable behavior | Detect other fan control software, warn user |
| Sleep/resume sensor loss | Temp spike | Re-enumerate hardware on resume, use safe defaults |

### Security Boundaries

```
┌──────────────────┐         ┌──────────────────┐
│     UI (User)    │◄───────►│  Service (SYSTEM)│
│                  │  Named  │                  │
│ - View sensors   │  Pipe   │ - Hardware access│
│ - Edit curves    │         │ - Apply controls │
│ - Request config │         │ - Write config   │
└──────────────────┘         └──────────────────┘
        │                            │
        │ No direct                  │ Full access
        │ hardware                   │ (ring 0 driver)
        ▼                            ▼
   [User Space]              [LibreHardwareMonitor]
```

## Edge Cases Handling

### Sensor Disappears Mid-Run
- Cache last 10 readings with timestamps
- If sensor missing >5s, use last known value + "STALE" flag
- If missing >30s, mark sensor as unavailable
- Trigger emergency fan mode if critical sensor lost

### Multiple GPUs
- Each GPU treated as separate device
- Independent curves per GPU
- Aggregate "hottest GPU" metric available for single-curve mode

### Laptop Systems
- Detect laptop via WMI chassis type
- Show warning: "Laptop detected - fan control likely unavailable"
- Still allow monitoring
- Check for vendor-specific control (some Lenovo/Dell expose WMI interfaces)

### Conflicting Software
- Check for running processes: `SpeedFan`, `FanControl`, `HWiNFO64`, `AIDA64`
- Warn user on startup
- Recommend closing conflicting apps

### Sleep/Resume
- Subscribe to `PowerModeChanged` event
- On resume:
  1. Wait 5 seconds for hardware to stabilize
  2. Re-enumerate all sensors
  3. Verify control still works
  4. Reapply current curve

## Performance Targets

| Metric | Target |
|--------|--------|
| Sensor poll latency | <50ms |
| UI update rate | 1Hz default (configurable 0.5-5Hz) |
| Service memory | <50MB |
| UI memory | <100MB |
| CPU usage (service) | <1% |
| CPU usage (UI) | <3% |
| Startup time | <3s |
