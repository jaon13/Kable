# 📋 Changelog

All notable changes to the **Kable** project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.0] - 2026-09-04

### Added
- **QA Test Engineering Master Plan & Suite**:
  - Comprehensive QA master plan and gap analysis specifications in `docs/qa_test_engineering/`.
  - 21 new test cases covering extreme byte fragmentation, sliding window framing, multi-segment UTF-8 boundaries, and 100-concurrent FIFO request fairness.
  - Automated TCP RST injection, NamedPipe server crash, and SerialPort cable disconnection fault-injection tests.
  - Total automated test suite expanded to **96 tests with 100% pass rate**.
- **Robustness & Protocol Violation Defense**:
  - `AsciiLineCodec` now enforces `MaxFrameSize` limit (default 64KB) to prevent unbounded memory growth (OOM) under delimiter absence.
  - Added `ProtocolViolationException` for protocol framing violations.
  - Re-entrant thread-safe `DisposeAsync` across all transport connection contexts.

---

## [1.0.0] - 2026-09-04

### Added
- **Bedrock Transport Layer (`Kable.Transports`)**:
  - `TcpConnectionContext` and `TcpConnectionFactory` with zero-delay socket pipelines.
  - `TcpConnectionListener` for server-side socket acceptance.
  - `NamedPipeConnectionContext` and `NamedPipeConnectionFactory` for ultra-fast local IPC.
  - `SerialPortConnectionContext` and `SerialPortConnectionFactory` for industrial RS-232C hardware.
- **Protocol Codec Engine (`Kable.Codecs`)**:
  - Zero-allocation `AsciiLineCodec` supporting custom delimiters, encodings, and autonomous alarm recognition.
  - `IProtocolCodec<T>` abstraction supporting correlation IDs and out-of-band message separation.
- **Reactive Device Session (`Kable.Engine`)**:
  - `KableSession<T>` implementing `IDeviceSession<T>`.
  - Hybrid Transaction Router: automatic FIFO lock serialization for legacy ASCII devices vs. lock-free interleaved multiplexing for correlation ID protocols.
  - RSocket-style interactions: `RequestAsync<T>`, `SendAsync`, `Stream`, and out-of-band `SendUrgentAsync`.
  - Fail-Fast safety policy: immediate dispatch of `DeviceDisconnectedException` upon link termination.
- **Tri-Stream Observability (`Kable.Observability`)**:
  - `CommObserver` with three independent bounded ringbuffers (`PeriodicTelemetry`, `AperiodicCommand`, `SpontaneousAlarm`).
  - `DropOldest` full mode preventing UI lagging under high-frequency telemetry floods.
- **Roslyn Incremental Source Generator (`Kable.Generators`)**:
  - `[DeviceCommand]` declarative attribute generating zero-allocation `IDeviceWireCommand` implementations.
  - Multi-parameter template string interpolation.
- **Dependency Injection & Fluent Builder (`Kable.Extensions`)**:
  - Fluent `KableClientBuilder<T>` for 3-line session initialization.
  - `AddKable()` and `AddKableSession<T>()` service extensions for Microsoft.Extensions.DependencyInjection.
- **Multi-Targeting**: Native cross-compilation support for `.NET 10.0`, `.NET 8.0 (LTS)`, and `.NET Standard 2.0`.
