# 04. Transport Fault Injection Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Transports`, `TcpConnectionContext`, `NamedPipeConnectionContext`, `SerialPortConnectionContext`

---

## 1. Overview & Verification Goals

Industrial field deployments subject communication conduits to electrical noise, physical cable detachment, and unexpected OS driver crashes. This specification verifies that `Kable` transports maintain deterministic cleanup, resource deallocation, and fail-fast behavior under injected physical faults.

---

## 2. Test Cases Specification

### TC-TRN-101: TCP RST Forced Packet Injection
- **Priority**: P0
- **Objective**: Simulate an abrupt server socket reset using `LingerState(true, 0)`. Verify that `TcpConnectionContext` detects the abortive disconnection, closes internal pipes, and dispatches `DeviceDisconnectedException`.
- **Assertion**: Active requests fail fast with `DeviceDisconnectedException`, and `IsConnected` switches to `false`.

### TC-TRN-102: NamedPipe Server Abrupt Termination
- **Priority**: P1
- **Objective**: Verify that when a local IPC partner process crashes abruptly, the client `NamedPipeConnectionContext` immediately catches EOF and terminates gracefully without hanging threads.
- **Assertion**: Pending requests throw `DeviceDisconnectedException`, and reader loops terminate.

### TC-TRN-103: SerialPort Physical Cable Detachment
- **Priority**: P1
- **Objective**: Verify that when a serial port handle is invalidated (simulating a physical USB-to-Serial unplug), `Abort()` and re-entrant `DisposeAsync()` calls complete safely without deadlocks.
- **Assertion**: `ConnectionClosed` token is canceled and streams are disposed cleanly.

### TC-TRN-104: Pipe Backpressure & Reader Stalling
- **Priority**: P1
- **Objective**: Verify that when remote readers stall, bursts of 500+ messages do not cause memory leaks or buffer corruption, and subsequent draining recovers all buffered packets.
- **Assertion**: All 500 items are drained without packet loss.

### TC-TRN-106: Unreachable Network Host & Connect Timeout
- **Priority**: P2
- **Objective**: Verify that connection attempts to an unroutable IP address or closed port respect cancellation tokens and raise clean network exceptions without hanging the process.
- **Assertion**: Throws `SocketException` or `OperationCanceledException` promptly.
