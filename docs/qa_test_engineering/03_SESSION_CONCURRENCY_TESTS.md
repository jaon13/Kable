# 03. Session Concurrency & Resilience Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Engine`, `KableSession<T>`, `IDeviceSession<T>`

---

## 1. Overview & Verification Goals

`KableSession` implements RSocket-style reactive interactions (`RequestAsync`, `SendAsync`, `Stream`, `SendUrgentAsync`). This specification verifies that the session state machine maintains strict ordering, lock fairness, zero deadlocks, and clean error isolation under high-concurrency contention and physical fault conditions.

---

## 2. Test Cases Specification

### TC-SES-101: 100 Concurrent FIFO Requests Fairness
- **Priority**: P0
- **Objective**: Verify that 100 simultaneous tasks dispatching `RequestAsync` on a non-correlation ASCII instrument maintain strict FIFO order without deadlocks or response misattribution.
- **Assertion**: Every task receives its corresponding `RESP_FIFO_CMD_{i}` response with 100% precision.

### TC-SES-102: Phantom Response Isolation After Timeout
- **Priority**: P0
- **Objective**: Verify that when request A times out and expires, a delayed response A arriving afterward does not satisfy or corrupt subsequent request B.
- **Assertion**: Request B matches its own `RESP_B`, and the phantom response A is diverted into `session.Stream`.

### TC-SES-103: Out-of-Band Urgent Command Preemption
- **Priority**: P1
- **Objective**: Verify that `SendUrgentAsync` bypasses an acquired `_fifoLock` during long-running measurements, flushing immediately to the underlying transport.
- **Assertion**: The emergency payload is transmitted without awaiting the FIFO lock.

### TC-SES-104: Mass Connection Disconnect Fail-Fast
- **Priority**: P0
- **Objective**: Verify that when a physical disconnection occurs while 50+ requests are awaiting responses, all callers are immediately aborted with `DeviceDisconnectedException` within milliseconds.
- **Assertion**: Total elapsed abort latency is under 3000ms (far before any request timeout triggers).

### TC-SES-105: Lifecycle Idempotency
- **Priority**: P2
- **Objective**: Verify that multiple concurrent calls to `StartAsync`, `StopAsync`, and `DisposeAsync` complete cleanly without throwing `ObjectDisposedException` or creating redundant sockets.

### TC-SES-107: Caller CancellationToken Expiration
- **Priority**: P1
- **Objective**: Verify that when a caller's `CancellationToken` expires mid-flight, the internal session lock is released and subsequent requests execute seamlessly.

### TC-SES-108: Observer Ringbuffer DropOldest Overflow
- **Priority**: P2
- **Objective**: Verify that high-frequency telemetry overflowing the bounded observer channel drops only the oldest records while preserving recent packets.
