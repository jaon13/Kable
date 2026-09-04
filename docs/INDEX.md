# Kable Reactive Hardware Communication Architecture (INDEX)

> This document serves as the master documentation index for **`Kable`**, a standardized industrial hardware communication conduit library combining **Microsoft Bedrock's transport abstraction (Pipelines / ConnectionContext)** with **RSocket's reactive interaction model (Request / Send / Stream)**.

---

## 💎 4 Confirmed Architectural Decisions

1. **Hybrid Transaction Engine**:
   - Legacy ASCII instruments without correlation tokens: Automatically serialized via an asynchronous preemptive FIFO lock (`SemaphoreSlim(1, 1)`), preventing response interleaving.
   - Modern protocols with Correlation IDs: Lock-free high-speed pipelining and out-of-order multiplexing.
2. **Fail-Fast Disconnection Safety Policy**:
   - Immediate dispatch of `DeviceDisconnectedException` upon link termination, driving physical hardware to an immediate safe state without dangerous blind retries.
3. **Strict Separation of Concerns**:
   - Core Communication Engine (`Kable`): Emits pure 0-GC telemetry records (`OnPacketTrace`) with zero file I/O overhead.
   - Logging & UI Rendering: Handled asynchronously by injected loggers and bounded UI ringbuffers (`ChannelReader`, `DropOldest`).
4. **Clean Legacy Elimination**:
   - Unified around the modern `IDeviceSession<T>` interface rather than maintaining incomplete legacy adapter shims.

---

## 🏛️ SSOT Master Specifications & Governance
- **[PROJECT_SPEC.md (Project Mission & Architecture)](file:///d:/Johnny/Kable/docs/PROJECT_SPEC.md)**: Core mission, 4 architectural decisions, 3-tier topology.
- **[SYSTEM_DESIGN.md (System Design & Interface Spec)](file:///d:/Johnny/Kable/docs/SYSTEM_DESIGN.md)**: `IConnectionContext`, `IProtocolCodec`, `IDeviceSession` specifications.
- **[CONVENTIONS.md (Coding Standards & Strict Rules)](file:///d:/Johnny/Kable/docs/CONVENTIONS.md)**: 300~500 line limits, prohibition of synchronous blocking, zero-GC guidelines.
- **[AGENTS.md (AI Agent Governance Rules)](file:///d:/Johnny/Kable/AGENTS.md)**: SSOT compliance, TDD, and micro-commit workflows.
- **[CONTRIBUTING.md (Development Guidelines)](file:///d:/Johnny/Kable/CONTRIBUTING.md)**: Multi-targeting, zero-allocation principles, PR standards.
- **[CHANGELOG.md (Release Version History)](file:///d:/Johnny/Kable/CHANGELOG.md)**: Semantic Versioning change log.

---

## 📚 Technical Documentation Index

### 1. [01. Architecture Overview](file:///d:/Johnny/Kable/docs/01_ARCHITECTURE_OVERVIEW.md)
- **Core Philosophy**: Bedrock Transport + RSocket Interaction 3-tier pipeline.
- **Hybrid Transaction Router**: Dynamic switching between FIFO serialization and lock-free interleaving.
- **Class Diagrams**: Integrated Mermaid architectural diagrams.

### 2. [02. Core Interfaces Specification](file:///d:/Johnny/Kable/docs/02_CORE_INTERFACES.md)
- **`IConnectionContext`**: Bedrock standard `PipeReader Input` / `PipeWriter Output` 0-GC pipe contracts.
- **`IProtocolCodec<T>`**: Bidirectional 0-allocation framing and autonomous message detection.
- **`IDeviceSession<T>`**: Reactive interaction API contracts (`RequestAsync`, `SendAsync`, `Stream`, `SendUrgentAsync`).
- **Standard Exceptions**: `DeviceDisconnectedException`, `DeviceTimeoutException`, `ProtocolViolationException`.

### 3. [03. Observability & Logging Specification](file:///d:/Johnny/Kable/docs/03_OBSERVABILITY_LOGGING.md)
- **Separation of Concerns**: Pure 0-GC in-engine dispatch vs. external disk logging.
- **Traffic Classification**: `TrafficKind` (Periodic Telemetry vs. Aperiodic Command vs. Spontaneous Alarm).
- **`ICommObserver`**: UI responsiveness guarantee via `DropOldest` bounded ringbuffer channels.

### 4. [04. Implementation & Directory Layout](file:///d:/Johnny/Kable/docs/04_IMPLEMENTATION_LAYOUT.md)
- **Standalone Repository**: Clean separation of `src/Kable`, `src/Kable.Generators`, and `tests/`.
- **Pure Namespaces**: `Kable.Core/Transports/Codecs/Engine/Exceptions/Observability/Generators`.
- **Packaging**: NuGet package consumption guide and Dependency Injection registration.

### 5. [05. Case Study: ICP-MS Multi-Vendor Integration](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/INDEX.md)
- **[01. Multi-Vendor Strategy](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/01_MULTI_VENDOR_STRATEGY.md)**: Agilent vs. PerkinElmer architectural comparison.
- **[02. Agilent Protocol Spec](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/02_AGILENT_PROTOCOL_SPEC.md)**: Agilent 7900/8900 ExtDevice RS-232C wire format.
- **[03. Agilent Architecture & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/03_AGILENT_ARCHITECTURE_DESIGN.md)**: Class diagrams, flowcharts, and FIFO timing models.
- **[04. Agilent Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/04_AGILENT_IMPLEMENTATION_CODE.md)**: Complete driver code and DI registration.
- **[05. PerkinElmer Syngistix Spec & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/05_PERKINELMER_SYNGISTIX.md)**: NamedPipe RPC specification and timing charts.
- **[06. PerkinElmer Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/06_PERKINELMER_IMPLEMENTATION.md)**: Driver source code and IPC configuration.

### 6. [06. Test Engineering & QA Master Plan](file:///d:/Johnny/Kable/docs/qa_test_engineering/INDEX.md)
- **[01. Gap Analysis](file:///d:/Johnny/Kable/docs/qa_test_engineering/01_GAP_ANALYSIS.md)**: Baseline audit and 6 critical dead zones identified.
- **[02. Codec & Framing Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/02_CODEC_AND_FRAMING_TESTS.md)**: OOM defense, 1-byte sliding windows, ArrayPool integrity.
- **[03. Session Concurrency Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/03_SESSION_CONCURRENCY_TESTS.md)**: Concurrent FIFO fairness, phantom response routing, fail-fast aborts.
- **[04. Transport Fault Injection Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/04_TRANSPORT_FAULT_INJECTION_TESTS.md)**: TCP RST injection, NamedPipe broken pipes, SerialPort cable removals.
- **[05. All New Test Cases Catalog](file:///d:/Johnny/Kable/docs/qa_test_engineering/05_ALL_NEW_TEST_CASES_CATALOG.md)**: Comprehensive 24-scenario test catalog matrix.
