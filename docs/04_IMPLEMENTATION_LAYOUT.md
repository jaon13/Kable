# 04. Implementation & Directory Layout

> This document defines the packaging structure, namespace taxonomy, and consumption guidelines for `Kable` as an independent, standalone NuGet infrastructure package (`Kable.nupkg`).

---

## 1. Standalone Git Repository Layout (`Kable/`)

`Kable` operates in an isolated repository with dedicated CI/CD, multi-targeting compilation, and independent test runners:

```
Kable/                                         # [Repository Root]
│
├── .github/workflows/ci-cd.yml                # Automated build, test, and NuGet package publish
├── Kable.sln                                  # Standalone solution
├── README.md                                  # Library overview and fluent quick-start
├── AGENTS.md                                  # Workspace rules and SSOT governance
├── CONTRIBUTING.md                            # Contribution guidelines
├── CHANGELOG.md                               # Version release log
│
├── src/
│   ├── Kable/                                 # [Kable Core Library]
│   │   ├── Kable.csproj                       # Multi-targeting NuGet package (net10.0, net8.0, netstandard2.0)
│   │   │
│   │   ├── Core/                              # [Transport Abstractions]
│   │   │   ├── IConnectionContext.cs          # PipeReader Input / PipeWriter Output / ConnectionClosed
│   │   │   ├── IConnectionFactory.cs          # Client factory abstraction (ConnectAsync)
│   │   │   └── IConnectionListener.cs         # Server listener abstraction (AcceptAsync)
│   │   │
│   │   ├── Transports/                        # [Transport Implementations]
│   │   │   ├── TcpConnection.cs               # TCP active client (NoDelay, Socket Pipelines)
│   │   │   ├── TcpListener.cs                 # TCP passive server listener
│   │   │   ├── NamedPipeConnection.cs         # High-speed local IPC pipe client
│   │   │   └── SerialPortConnection.cs        # Industrial RS-232/422/485 serial client
│   │   │
│   │   ├── Codecs/                            # [Zero-Allocation Codecs]
│   │   │   ├── IProtocolCodec.cs             # TryDecode / Encode / SupportsCorrelationId
│   │   │   └── AsciiLineCodec.cs             # Delimiter-based framing with MaxFrameSize guard
│   │   │
│   │   ├── Engine/                            # [Reactive Session Engine]
│   │   │   ├── IDeviceSession.cs             # RequestAsync / SendAsync / Stream / SendUrgentAsync
│   │   │   └── KableSession.cs               # Hybrid FIFO lock + interleaved multiplexing
│   │   │
│   │   ├── Exceptions/                        # [Fail-Fast Exception Hierarchy]
│   │   │   ├── DeviceDisconnectedException.cs # Immediate link termination abort
│   │   │   ├── DeviceTimeoutException.cs      # Watchdog deadline expiry
│   │   │   └── ProtocolViolationException.cs  # Frame length & invariant violations
│   │   │
│   │   ├── Observability/                     # [Traffic Separator & RingBuffers]
│   │   │   ├── ICommObserver.cs              # PeriodicStream / CommandStream / AlarmStream
│   │   │   ├── PacketTraceRecord.cs          # 0-GC struct event record
│   │   │   └── CommObserver.cs               # Bounded channel dispatcher (DropOldest ringbuffers)
│   │   │
│   │   └── Extensions/                        # [Fluent Builder & DI Registration]
│   │       ├── KableClientBuilder.cs         # Fluent 3-line configuration builder
│   │       └── KableServiceExtensions.cs     # Microsoft DI container extensions
│   │
│   └── Kable.Generators/                      # [Roslyn Incremental Source Generator]
│       ├── Kable.Generators.csproj            # Analyzer & Generator packaging
│       ├── DeviceCommandAttribute.cs         # [DeviceCommand] marker attribute
│       └── ProtocolSourceGenerator.cs        # Compile-time zero-allocation wire formatter
│
└── tests/
    ├── Kable.Tests/                           # Runtime unit, concurrency, and fault injection tests
    │   ├── Cases/
    │   │   ├── Codecs/
    │   │   ├── Engine/
    │   │   ├── Transports/
    │   │   └── Observability/
    │   └── Fixtures/
    │
    └── Kable.Generators.Tests/                # Isolated source generator tests
```

---

## 2. Clean Namespace Taxonomy

`Kable` avoids application-specific prefixes, adhering to standard .NET BCL conventions:

| Layer | Namespace | Responsibility |
| :--- | :--- | :--- |
| **Core** | `Kable.Core` | Bedrock transport abstraction (`IConnectionContext`) |
| **Transports** | `Kable.Transports` | TCP, SerialPort, and NamedPipe concrete implementations |
| **Codecs** | `Kable.Codecs` | Zero-allocation framing and protocol encoders/decoders |
| **Engine** | `Kable.Engine` | Reactive interaction sessions (`IDeviceSession`, `KableSession`) |
| **Exceptions**| `Kable.Exceptions`| Fail-Fast industrial exception contracts |
| **Observability**| `Kable.Observability`| Multi-stream UI ringbuffer channels |
| **Generators**| `Kable.Generators`| Compile-time source generation attributes |
| **IoC DI** | `Microsoft.Extensions.DependencyInjection` | Service registration extension methods |

---

## 3. Package Consumption Guide

Consuming projects reference `Kable` as a clean NuGet package without direct source coupling:

### Step 1. Reference Package in Project File (`MyProject.csproj`)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Kable" Version="1.1.0" />
  </ItemGroup>
</Project>
```

### Step 2. Dependency Injection Setup (`Program.cs`)
```csharp
using Kable;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 1. Register Kable core infrastructure
services.AddKable();

// 2. Configure dedicated hardware session
services.AddKableSession<string>((builder, sp) =>
{
    builder.UseSerialPort("COM3", baudRate: 9600)
           .UseCodec(new AsciiLineCodec(delimiter: 0x0D));
});
```
