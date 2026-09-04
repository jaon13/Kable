# 02. Core Interfaces Specification

> This document specifies the transport-level contracts based on `System.IO.Pipelines` alongside the upper-level RSocket reactive session interfaces.

---

## 1. Bedrock Lower Transport Context (`IConnectionContext`)

The core specification unifying all physical (TCP, Serial) and logical (NamedPipe IPC) connections:

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
    
    // Bedrock Standard 0-GC Pipelines
    PipeReader Input { get; }
    PipeWriter Output { get; }
    
    // Disconnection Notification Token
    CancellationToken ConnectionClosed { get; }
    
    void Abort(string reason);
}

public interface IConnectionFactory
{
    ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default);
}

public interface IConnectionListener : IAsyncDisposable
{
    ValueTask<IConnectionContext> AcceptAsync(CancellationToken ct = default);
    void Stop();
}
```

---

## 2. Protocol Codec Interface (`IProtocolCodec<TMessage>`)

A bidirectional zero-allocation transformer bridging the raw wire byte sequence and typed domain messages:

```csharp
namespace Kable.Codecs;

using System.Buffers;

public interface IProtocolCodec<TMessage>
{
    // Indicates whether the protocol supports correlation tokens natively.
    // If false, the session engine applies a preemptive FIFO lock (SemaphoreSlim)
    // to guarantee responses are not misattributed under concurrent invocations.
    bool SupportsCorrelationId { get; }

    // Decodes a complete message frame from the input sequence without allocations
    bool TryDecode(ref ReadOnlySequence<byte> buffer, out TMessage message);
    
    // Encodes a message into the pipe output writer buffer
    void Encode(TMessage message, IBufferWriter<byte> output);
    
    // Extracts correlation token for request-response matching
    string? ExtractCorrelationId(TMessage message);

    // Identifies whether an incoming frame is unsolicited telemetry/heartbeat/alarm
    // (If true, it is routed directly to the Stream channel rather than waking request callers)
    bool IsAutonomousMessage(TMessage message) => false;
}
```

---

## 3. Upper Reactive Session Interface (`IDeviceSession<TMessage>`)

The primary public-facing unified interface for hardware control and telemetry subscription:

```csharp
namespace Kable.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IDeviceSession<TMessage> : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }
    
    // 1. Real-time telemetry stream subscription (IAsyncEnumerable / Channel)
    IAsyncEnumerable<TMessage> Stream { get; }
    
    // 2. Unidirectional notification / command dispatch (Fire-and-Forget)
    ValueTask SendAsync(TMessage message, CancellationToken ct = default);
    
    // 3. Request-Response RPC (Hybrid FIFO Lock or Lock-Free Interleaved + Watchdog Isolation)
    // Throws DeviceDisconnectedException on connection drop, or DeviceTimeoutException on deadline expiry
    ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default);
    
    // 4. Out-of-Band Emergency Stop Injection
    ValueTask SendUrgentAsync(TMessage urgentMessage);
    
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync();
}
```

---

## 4. Industrial Fail-Fast Exception Hierarchy

```csharp
namespace Kable.Exceptions;

using System;

/// <summary>
/// Dispatched immediately to all active pending callers upon physical link termination (Fail-Fast).
/// </summary>
public class DeviceDisconnectedException : Exception
{
    public DeviceDisconnectedException(string message) : base(message) { }
    public DeviceDisconnectedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Dispatched when an instrument fails to respond within the configured deadline.
/// </summary>
public class DeviceTimeoutException : TimeoutException
{
    public string Command { get; }
    public TimeSpan Timeout { get; }

    public DeviceTimeoutException(string command, TimeSpan timeout)
        : base($"Device command '{command}' timed out after {timeout.TotalSeconds:F1}s.")
    {
        Command = command;
        Timeout = timeout;
    }
}

/// <summary>
/// Dispatched when frame length bounds or protocol invariants are breached.
/// </summary>
public class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message) : base(message) { }
    public ProtocolViolationException(string message, Exception innerException) : base(message, innerException) { }
}
```
