# 🏛️ Kable - System Design & Interface Specification

> **Document Status**: Single Source of Truth (SSOT)  
> **Last Updated**: 2026-09-04  
> **Related Documents**: [PROJECT_SPEC.md](file:///d:/Johnny/Kable/docs/PROJECT_SPEC.md), [CONVENTIONS.md](file:///d:/Johnny/Kable/docs/CONVENTIONS.md), [02_CORE_INTERFACES.md](file:///d:/Johnny/Kable/docs/02_CORE_INTERFACES.md)

---

## 1. Transport Abstraction Layer (`Kable.Core` & `Kable.Transports`)

### 1.1 `IConnectionContext`
Standard full-duplex 0-GC pipeline context aligned with Microsoft Bedrock and ASP.NET Core Kestrel standards:

```csharp
namespace Kable.Core;

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

public interface IConnectionContext : IAsyncDisposable
{
    string ConnectionId { get; }
    string EndpointDescription { get; }
    PipeReader Input { get; }
    PipeWriter Output { get; }
    CancellationToken ConnectionClosed { get; }
    void Abort(string reason);
}
```

- **`TcpConnectionContext`**: Enforces `Socket.NoDelay = true`, bound directly to asynchronous `NetworkStream` pipeline readers and writers.
- **`NamedPipeConnectionContext`**: High-speed Windows/Linux IPC pipe stream binding.
- **`SerialPortConnectionContext`**: Industrial RS-232C physical stream binding with hardware signal configuration (DTR/RTS).

---

## 2. Serialization & Framing Layer (`Kable.Codecs`)

### 2.1 `IProtocolCodec<TMessage>`
Converts raw byte sequences from the transport pipeline into typed messages with zero heap allocations:

```csharp
namespace Kable.Codecs;

using System.Buffers;

public interface IProtocolCodec<TMessage>
{
    bool SupportsCorrelationId { get; }
    bool TryDecode(ref ReadOnlySequence<byte> buffer, out TMessage message);
    void Encode(TMessage message, IBufferWriter<byte> output);
    string? ExtractCorrelationId(TMessage message);
    bool IsAutonomousMessage(TMessage message);
}
```

- **`AsciiLineCodec`**: Delimiter-based (`\n`, `\r\n`) framing with built-in `MaxFrameSize` (default 64KB) memory guard against Out-Of-Memory (OOM) exploits.
- **Autonomous Message Detection (`IsAutonomousMessage`)**: Instantly detects spontaneous alarms and telemetry headers (e.g. `$`, `#`).

---

## 3. Interaction & Session Engine Layer (`Kable.Engine`)

### 3.1 `IDeviceSession<TMessage>`
RSocket-style 4-way reactive interaction interface:

```csharp
namespace Kable.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IDeviceSession<TMessage> : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }
    IAsyncEnumerable<TMessage> Stream { get; }
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync();
    ValueTask SendAsync(TMessage message, CancellationToken ct = default);
    ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default);
    ValueTask SendUrgentAsync(TMessage urgentMessage);
}
```

### 3.2 Hybrid Transaction Routing Mechanics
- `_codec.SupportsCorrelationId == false`:
  - Acquires `_fifoLock.WaitAsync()` $\rightarrow$ Flushes command $\rightarrow$ Awaits response $\rightarrow$ Releases lock.
- `_codec.SupportsCorrelationId == true`:
  - Registers entry in `ConcurrentDictionary<string, TaskCompletionSource>` without locking $\rightarrow$ Flushes command $\rightarrow$ Asynchronously matches out-of-order responses.
- **Phantom Response Isolation**: Responses arriving after a timeout are automatically routed to the unhandled `_incomingStream` channel, preventing pollution of subsequent requests.

---

## 4. Observability & Telemetry Layer (`Kable.Observability`)

### 4.1 Tri-Stream Bounded Ringbuffer Architecture
- **`PeriodicTelemetry`**: High-frequency real-time telemetry (configured with `DropOldest` to eliminate UI stutter).
- **`AperiodicCommand`**: Audit history of dispatched control commands and matching responses.
- **`SpontaneousAlarm`**: High-priority unsolicited instrument alarms and interlock alerts.
