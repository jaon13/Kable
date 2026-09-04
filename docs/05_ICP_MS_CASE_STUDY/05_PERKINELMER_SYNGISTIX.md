# 05. PerkinElmer Syngistix Case Study

> This document specifies the integration architecture for PerkinElmer NexION ICP-MS instruments (Syngistix software remote automation) using `Kable`.

---

## 1. PerkinElmer Syngistix Protocol Characteristics

- **Transport Medium**: High-speed TCP/IP network socket or local gRPC NamedPipe
- **Interaction Model**:
  - Remote method loading (`LoadMethod`).
  - Peristaltic pump speed regulation (`StartPump`), settling delay (`MethodSettleTime`).
  - Sample acquisition triggering (`StartAcquisition`).
  - Continuous status polling (`GetInstrumentStatusAsync`) and acquisition result ingestion (`GetAcquisitionResult`).

---

## 2. Protocol & RPC Operation Specification

| Direction | Operation | Source API Mapping | Traffic Kind | Description |
| :---: | :--- | :--- | :---: | :--- |
| **TX** | **Ignite Plasma** | `m_Client.StartPlasma()` | `AperiodicCommand` | RF plasma ignition RPC call |
| **TX** | **Extinguish Plasma** | `m_Client.StopPlasma()` | `AperiodicCommand` | RF plasma extinguishing RPC call |
| **TX** | **Load Method** | `m_Client.LoadMethod(folder, name)` | `AperiodicCommand` | Loads `.mth` analytical method template |
| **TX** | **Start Pump** | `m_Client.StartPump(dSpeedRPM)` | `AperiodicCommand` | Activates peristaltic pump at specified RPM |
| **TX** | **Stop Pump** | `m_Client.StopPump()` | `AperiodicCommand` | Stops sample delivery pump |
| **TX** | **Start Acquisition** | `m_Client.StartAcquisition(sampleId)` | `AperiodicCommand` | Starts measurement sequence for sample |
| **TX** | **Stop Acquisition** | `m_Client.StopAcquisition()` | `AperiodicCommand` | Emergency sequence termination |
| **TX/RX** | **Poll Status** | `m_Client.GetInstrumentStatusAsync()` | `PeriodicTelemetry` | Queries vacuum, RF power, and interlocks |
| **RX** | **Acquisition Stream** | `m_Client.AcquisitionResultEvent` | `SpontaneousAlarm` | Measurement completion CPS and intensity event |

---

## 3. Class Architecture

```mermaid
classDiagram
    %% Declarative RPC Client
    class ISyngistixRpcClient {
        <<interface>>
        +StartPlasmaAsync(ct) ValueTask~PeStatusResponse~
        +StopPlasmaAsync(ct) ValueTask~PeStatusResponse~
        +LoadMethodAsync(folder, name, ct) ValueTask~PeStatusResponse~
        +StartPumpAsync(rpm, ct) ValueTask~PeStatusResponse~
        +StartAcquisitionAsync(sampleId, ct) ValueTask~PeStatusResponse~
        +StopAcquisitionUrgentAsync() ValueTask
    }

    %% Inbound Packets
    class IPerkinElmerPacket {
        <<interface>>
    }
    class PeStatusResponse {
        +bool Success
        +string ErrorMessage
    }
    class PeInstrumentStatusEvent {
        +double VacuumLevelPa
        +double PlasmaPowerWatts
        +string StatusMessage
    }
    class PeAcquisitionCompletedEvent {
        +string SampleId
        +IReadOnlyList~double~ Intensities
    }

    IPerkinElmerPacket <|.. PeStatusResponse
    IPerkinElmerPacket <|.. PeInstrumentStatusEvent
    IPerkinElmerPacket <|.. PeAcquisitionCompletedEvent

    %% Driver & Codec
    class PerkinElmerProtocolCodec {
        +bool SupportsCorrelationId: true
        +TryDecode() bool
        +Encode() void
    }
    class IIcpmsDriver {
        <<interface>>
        +IgnitePlasmaAsync() Task~bool~
        +ExtinguishPlasmaAsync() Task~bool~
        +StartBatchAsync() Task~BatchRunStatus~
        +AbortBatchAsync() Task~bool~
    }
    class PerkinElmerDeviceDriver {
        -ISyngistixRpcClient _rpc
        +IgnitePlasmaAsync() Task~bool~
        +StartBatchAsync() Task~BatchRunStatus~
        +AbortBatchAsync() Task~bool~
    }

    IIcpmsDriver <|.. PerkinElmerDeviceDriver
    PerkinElmerDeviceDriver o-- ISyngistixRpcClient
    PerkinElmerProtocolCodec ..> IPerkinElmerPacket
```

---

## 4. Sequence Diagram

```mermaid
sequenceDiagram
    autonumber
    actor User as Host UI / Operator
    participant Driver as PerkinElmerDeviceDriver
    participant Proxy as ISyngistixRpcClient (Generated Proxy)
    participant Session as KableSession (Interleaving)
    participant HW as PerkinElmer NexION (Syngistix)

    Note over User, HW: 1. Method Loading & Peristaltic Pump Start
    User->>Driver: StartBatchAsync("PE-BATCH-01")
    Driver->>Proxy: LoadMethodAsync(@"C:\PE\Methods", "TraceMetal.mth")
    Proxy->>Session: RequestAsync(RpcEnvelope)
    Session->>HW: TCP/gRPC: LoadMethod
    HW-->>Session: OK
    Session-->>Proxy: PeStatusResponse(Success = true)
    Proxy-->>Driver: PeStatusResponse

    Driver->>Proxy: StartPumpAsync(20.0)
    Proxy->>Session: RequestAsync(RpcEnvelope)
    Session->>HW: TCP/gRPC: StartPump(20 RPM)
    HW-->>Session: OK
    Session-->>Proxy: PeStatusResponse(Success = true)
    Proxy-->>Driver: PeStatusResponse

    Note over User, HW: 2. Sequence Start & Background Telemetry
    Driver->>Proxy: StartAcquisitionAsync("SMP-001")
    Proxy->>Session: RequestAsync(RpcEnvelope)
    Session->>HW: TCP/gRPC: StartAcquisition
    HW-->>Session: OK
    Session-->>Proxy: PeStatusResponse(Success = true)
    Proxy-->>Driver: PeStatusResponse
    Driver-->>User: Batch Start OK

    Note over Session, HW: Background status telemetry streaming
    HW-->>Session: PeInstrumentStatusEvent (Vacuum / Power)
    Session-->>Driver: Routed to UI dashboard ringbuffers

    Note over Session, HW: Unsolicited result notification upon completion
    HW-->>Session: PeAcquisitionCompletedEvent(SampleId = "SMP-001")
    Session-->>Driver: Directly forwarded to quantification engine!
```
