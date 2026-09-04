# 05. All New Test Cases Catalog

> **Document Status**: Approved Test Catalog  
> **Total Test Cases**: 24 Target Scenarios (P0: 8, P1: 10, P2: 6) across 4 domain layers.

---

## 1. Codecs & Framing Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-COD-101** | Security / Safety | **P0** | `Codec_InfiniteStreamWithoutDelimiter_ThrowsProtocolViolationException` | Throws `ProtocolViolationException` when 1MB+ streams lack delimiters |
| **TC-COD-102** | Framing | **P1** | `Codec_SingleByteSlidingWindow_ReassemblesCompleteMessage` | Reassembles a 100-byte message across 1-byte sequence segments |
| **TC-COD-103** | Edge Case | **P2** | `Codec_ConsecutiveDelimiters_EmitsEmptyFramesWithoutException` | Decodes empty messages sequentially on `\r\n\r\n\n\n` without bounds errors |
| **TC-COD-104** | Encoding | **P1** | `Codec_MultiByteUtf8SplitAcrossSegments_PreservesCharacters` | Decodes multi-byte UTF-8 sequences cleanly across segment boundaries |
| **TC-COD-105** | Memory Integrity | **P0** | `Codec_ArrayPoolRentAndReturn_MaintainsPerfectBalance` | Ensures rented `ArrayPool<byte>` buffers are returned in `finally` |
| **TC-COD-106** | Extensibility | **P1** | `Codec_BinaryLengthPrefixedHeader_WaitsForFullBody` | Inspects binary length fields and waits for complete payloads |

---

## 2. Engine & Session Concurrency Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-SES-101** | Concurrency / Stress | **P0** | `Session_200ConcurrentFifoRequests_MaintainsStrictOrderWithoutDeadlock` | Strict 1:1 request/response matching across 100+ concurrent tasks |
| **TC-SES-102** | Fault Isolation | **P0** | `Session_LateResponseAfterTimeout_RoutesToStreamWithoutPollutingNextRequest` | Diverts expired responses to `Stream`, preventing corruption of next request |
| **TC-SES-103** | Priority | **P1** | `Session_SendUrgentAsync_BypassesFifoLockInstantly` | Emergency commands bypass acquired `_fifoLock` immediately |
| **TC-SES-104** | Fault Tolerance | **P0** | `Session_AbruptDisconnect_Cancels100PendingRequestsFailFast` | 50+ waiting callers fail fast with `DeviceDisconnectedException` within ms |
| **TC-SES-105** | Lifecycle | **P2** | `Session_MultipleStartAndStop_MaintainsIdempotency` | Multiple concurrent starts/stops preserve single socket and idempotency |
| **TC-SES-106** | Concurrency | **P1** | `Session_CorrelationIdCollisionDefense_ThrowsOrReplacesSafely` | Safe isolation when duplicate correlation IDs are issued |
| **TC-SES-107** | Cancellation | **P1** | `Session_CallerCancellationTokenExpired_CancelsWithoutCorruptingEngine` | Caller token expiration releases session locks for subsequent requests |
| **TC-SES-108** | Observability | **P2** | `Session_ObserverQueueOverflow_DropsOldestAndPreservesLatestPacket` | Bounded ringbuffer drops oldest items, keeping recent telemetry |

---

## 3. Transport Fault Injection Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-TRN-101** | Socket Fault | **P0** | `Tcp_ForceResetRstPacket_AbortsConnectionContextInstantly` | Socket linger reset (RST) immediately propagates fail-fast disconnection |
| **TC-TRN-102** | Process Fault | **P1** | `NamedPipe_ServerProcessAbruptTermination_DetectsPipeBroken` | Abrupt IPC process termination terminates pipe reader cleanly |
| **TC-TRN-103** | Hardware Fault | **P1** | `SerialPort_PhysicalCableRemoval_HandlesBaseStreamDisposed` | Unplugged USB-to-Serial port triggers clean non-blocking disposal |
| **TC-TRN-104** | Backpressure | **P1** | `Pipe_BackpressureThresholdExceeded_PausesWriterUntilDrain` | 500+ unread frames trigger backpressure and drain completely |
| **TC-TRN-105** | Performance / GC | **P1** | `Benchmark_10000TelemetryPackets_ZeroGen2GarbageCollection` | 10,000-packet ingestion triggers 0 Gen2 garbage collections |
| **TC-TRN-106** | Network | **P2** | `Tcp_ConnectionTimeoutToNonRoutableIp_ThrowsCleanTimeoutException` | Unreachable hosts respect timeout and cancel cleanly |

---

## 4. Source Generator & Extensions Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-GEN-101** | Diagnostics | **P1** | `Generator_MissingTemplateProperty_EmitsDiagnosticWarningOrError` | Emits compile diagnostics on invalid template properties |
| **TC-GEN-102** | Functionality | **P1** | `Generator_MultiParamRecords_InterpolatesAllParametersCorrectly` | Multi-parameter record structs format wire templates with 100% precision |
| **TC-GEN-103** | DI Validation | **P2** | `Builder_MissingCodecOrFactory_ThrowsDescriptiveInvalidOperationException` | Descriptive `InvalidOperationException` on missing builder components |
| **TC-GEN-104** | DI Lifecycle | **P2** | `ServiceCollection_AddKableSession_ResolvesCorrectSingletonOrScoped` | Default DI configuration resolves singletons correctly |
