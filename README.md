# 🔌 Kable

> **High-Performance, Zero-Allocation Reactive Hardware Communication Engine for .NET**  
> Combining Microsoft Bedrock's `System.IO.Pipelines` transport abstraction with RSocket interaction patterns.

---

## ✨ Key Features

- **Pure Multi-Targeting**: Native support for `.NET 10.0`, `.NET 8.0 (LTS)`, and `netstandard2.0` (.NET Framework 4.8 / Legacy systems).
- **0-GC Pipelines I/O**: Zero memory copies and vector-accelerated buffer parsing via `System.IO.Pipelines` and `ReadOnlySequence<byte>`.
- **Hybrid Transaction Router**:
  - **No-Correlation ID Devices** (RS-232C, Simple ASCII): Automatic asynchronous preemptive FIFO lock (`SemaphoreSlim`) preventing request interleaving.
  - **Correlation ID Protocols** (Modern TCP/IPC): High-speed lock-free pipelining & interleaving.
- **Fail-Fast Safety Policy**: Immediate `DeviceDisconnectedException` dispatch upon cable/link disconnection to guarantee physical hardware safe-state.
- **Tri-Stream Observability**: Independent bounded ringbuffers (`DropOldest`) separating Periodic Telemetry, Command Console, and Spontaneous Alarms to prevent UI lagging.

---

## 🚀 Quick Start

```bash
dotnet add package Kable
```

```csharp
using Kable.Core;
using Kable.Engine;
using Kable.Codecs;

// 1. Create a reactive Kable session
await using var session = new KableSession<string>(
    connectionFactory: connectionFactory,
    codec: new AsciiLineCodec()
);

await session.StartAsync();

// 2. Request-Response with Fail-Fast Watchdog
string response = await session.RequestAsync<string>("START_ACQUISITION", TimeSpan.FromSeconds(3));

// 3. Subscribe to Real-time Stream
await foreach (var packet in session.Stream)
{
    Console.WriteLine($"Received telemetry: {packet}");
}
```

---

## 📄 License
MIT License
