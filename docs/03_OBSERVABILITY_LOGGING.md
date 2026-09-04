# 03. Observability & Logging Specification

> This document defines the `ICommObserver` multi-channel observability architecture, separating periodic telemetry from control commands to eliminate UI lag and ensure compliance logging.

---

## 1. Background & Philosophy: Strict Separation of Concerns

- **Duty of the Communication Engine (`Kable`)**:
  - Focuses strictly on physical I/O and packet transmission.
  - Disk file writes, log rotation, and database insertions are completely excluded from the engine core. (Emits pure 0-GC struct events).
- **Duty of External Injected Loggers (`Serilog` / DB)**:
  - Lossless archival required for regulatory compliance (e.g. FDA 21 CFR Part 11) is performed asynchronously by external logging pipelines.
- **Duty of UI Monitoring**:
  - The real-time user interface isolates periodic telemetry (sensors) from control console logs using **independent bounded ringbuffers (`DropOldest`)**, preventing UI stutter and memory leaks.

---

## 2. Multi-Channel Observability Contract (`ICommObserver`)

```csharp
namespace Kable.Observability;

using System;
using System.Threading.Channels;

public enum TrafficKind
{
    /// <summary>
    /// Aperiodic control commands and transaction responses (user actions, sequence runs, E-STOP).
    /// </summary>
    AperiodicCommand,

    /// <summary>
    /// Periodic status polling and telemetry (heartbeats, cyclic sensors, ping-pong).
    /// </summary>
    PeriodicTelemetry,

    /// <summary>
    /// Spontaneous unsolicited instrument alarms and interlock notifications.
    /// </summary>
    SpontaneousAlarm
}

public readonly struct PacketTraceRecord
{
    public DateTime TimestampUtc { get; }
    public PacketDirection Direction { get; }     // Tx or Rx
    public TrafficKind Kind { get; }              // Traffic classification
    public string Tag { get; }                    // Identifier tag (e.g., "TEMP_POLL", "VALVE_CMD")
    public ReadOnlyMemory<byte> RawBytes { get; } // 0-allocation buffer slice
    public string? ParsedText { get; }
    public TimeSpan Latency { get; }              // Round-trip response latency
}

public interface ICommObserver
{
    // [Engine -> Observer 0-GC Notification]
    // The engine calls this non-blocking method and returns immediately to the I/O loop.
    void OnPacketTrace(in PacketTraceRecord trace);

    // [UI Dedicated Decoupled Channels with DropOldest Ringbuffers]
    // A. Cyclic telemetry stream -> Binds directly to top-level real-time gauges / charts
    ChannelReader<PacketTraceRecord> PeriodicStream { get; }

    // B. Aperiodic command/response stream -> Binds to command console (scroll lock free)
    ChannelReader<PacketTraceRecord> CommandStream { get; }

    // C. Error/alarm stream -> Binds to alert popups and fault logs
    ChannelReader<PacketTraceRecord> AlarmStream { get; }
}
```

---

## 3. Logging & Storage Integration Guide

The core engine connects seamlessly with the wider .NET ecosystem via `ICommObserver`:

1. **Persistent Disk Archival (`Serilog` Integration)**:
   - Route `OnPacketTrace` into dedicated Serilog sub-loggers.
   - `AperiodicCommand` and `SpontaneousAlarm` events are written losslessly to daily rolling audit logs (`commands-.log`).
   - `PeriodicTelemetry` is aggregated into metrics (`Meter`/`Gauge`) or written to separate compressed binary files, avoiding unnecessary disk churn.
2. **UI Thread Freeze Prevention (`BoundedChannelFullMode.DropOldest`)**:
   - The UI-bound `PeriodicStream` uses a bounded channel (e.g. capacity = 1,000) that automatically drops stale historical points when consumer processing falls behind, guaranteeing fluid 60 FPS UI rendering even during 100Hz streaming bursts.
