# 01. Test Suite Audit & Gap Analysis

> **Document Status**: Approved Baseline  
> **Scope**: `src/Kable`, `src/Kable.Generators`, `tests/Kable.Tests`, `tests/Kable.Generators.Tests`

---

## 1. Baseline Test Analysis

The original test suite demonstrated high reliability in standard operations, verifying single/multi-segment ASCII line framing, clean connection abortion, and `CommObserver` bounded channel overflow.

However, an in-depth audit from an industrial hardware reliability perspective revealed **6 critical operational dead zones** susceptible to production failure under harsh industrial noise, cable detachment, or high-throughput concurrency.

---

## 2. 6 Critical Architectural Dead Zones Identified

### 🔴 GAP 1: Codec Buffer Overflow Under Missing Delimiters (OOM Risk)
- **Vulnerability**: If an instrument or noise source streams continuous data without the expected line delimiter, `PipeReader` continuously buffers incoming chunks, leading to unbounded heap growth and process termination.
- **Remediation & Test**: Enforce and verify `MaxFrameSize` (default 64KB), throwing a clear `ProtocolViolationException` upon threshold breach.

### 🔴 GAP 2: Phantom Responses Polluting Subsequent Requests
- **Vulnerability**: When request A times out and request B is immediately dispatched, if request A's response arrives late (a phantom packet), it could erroneously satisfy request B under FIFO routing.
- **Remediation & Test**: Ensure late-arriving responses are safely intercepted and redirected to the unsolicited `Stream` without corrupting active requests.

### 🔴 GAP 3: Physical Serial Port Detachment & Thread Hangs
- **Vulnerability**: Physical USB-to-Serial converter disconnection often causes underlying stream handles to hang or fail uncleanly during teardown.
- **Remediation & Test**: Verify re-entrant `DisposeAsync` and immediate cancellation token propagation on port failure.

### 🟡 GAP 4: TCP RST & Silent Disconnection (Half-Open Sockets)
- **Vulnerability**: Abrupt TCP resets (RST flag without FIN handshake) can cause pipeline readers to deadlock if socket options are misconfigured.
- **Remediation & Test**: Verify immediate `DeviceDisconnectedException` dispatch upon socket reset.

### 🟡 GAP 5: High-Concurrency FIFO Fairness & Lock Contention
- **Vulnerability**: Hundreds of concurrent callers competing for `_fifoLock` can lead to starvation or response ordering inversion if scheduling is unbuffered.
- **Remediation & Test**: Heavy stress testing with 100+ concurrent requests ensuring strict 1:1 request-response correspondence.

### 🟡 GAP 6: Roslyn Incremental Generator Isolation & Diagnostics
- **Vulnerability**: Generated partial commands must integrate cleanly without namespace collisions or syntax diagnostics.
- **Remediation & Test**: Isolated test project (`Kable.Generators.Tests`) testing multi-parameter record struct commands.

---

## 3. Priority Matrix

| Priority | Layer | Gap ID | Description | Resolution |
| :--- | :--- | :--- | :--- | :--- |
| **P0** | Codecs | GAP-01 | OOM from missing delimiter | Enforce `MaxFrameSize` + `ProtocolViolationException` |
| **P0** | Engine | GAP-02 | Phantom packet pollution | Route expired responses to `Stream` |
| **P0** | Engine | GAP-05 | FIFO lock fairness & ordering | 100-thread concurrent stress testing |
| **P1** | Transport | GAP-03 | Serial port physical removal | Safe teardown and non-blocking `DisposeAsync` |
| **P1** | Transport | GAP-04 | TCP RST packet handling | Socket abort fail-fast verification |
| **P1** | Generators | GAP-06 | Multi-param command generation | Dedicated `Kable.Generators.Tests` project |
