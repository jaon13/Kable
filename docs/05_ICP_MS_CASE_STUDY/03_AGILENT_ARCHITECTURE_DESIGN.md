# 03. Agilent Architecture & Design

> This document defines the module layout, class diagrams, data flowcharts, and interaction timing models for the Agilent MassHunter ICP-MS driver module.

---

## 1. Modular Project Layout

Packaged as an isolated project (`src/Icpms.MassHunter`), maintaining file sizes strictly under 300 lines in compliance with `CONVENTIONS.md`:

```
src/Icpms.MassHunter/                        # [Driver Module Root]
│
├── Icpms.MassHunter.csproj                  # .NET 10 Class Library
│
├── Protocol/                                # [Protocol Layer: Icpms.MassHunter.Protocol]
│   ├── MassHunterCommands.cs                # [DeviceCommand] Outbound command declarations
│   ├── MassHunterPackets.cs                 # Inbound packet records and event models
│   └── MassHunterProtocolCodec.cs           # Zero-allocation delimiter framing codec
│
├── Driver/                                  # [Domain Driver: Icpms.MassHunter]
│   ├── IIcpmsDriver.cs                      # Unified domain business contract
│   ├── MassHunterDeviceDriver.cs            # FSM state control & stream orchestration
│   └── MassHunterDriverOptions.cs           # Serial port options model
│
└── Extensions/                              # [IoC Registration]
    └── MassHunterServiceExtensions.cs       # AddMassHunterDeviceDriver DI extension
```

---

## 2. Class Diagram

```mermaid
classDiagram
    %% Outbound Commands
    class IMassHunterCommand {
        <<interface>>
        +FormatWireMessage() string
    }
    class IgnitePlasmaCommand {
        +FormatWireMessage() string ("oPON")
    }
    class ExtinguishPlasmaCommand {
        +FormatWireMessage() string ("oPOFF")
    }
    class StartBatchScriptCommand {
        +string ScriptPath
        +FormatWireMessage() string ("oBATCH...")
    }
    class AppendSampleCommand {
        +int SampleIndex
        +FormatWireMessage() string ("oAPPEND...")
    }
    class EmergencyAbortCommand {
        +FormatWireMessage() string ("oRESUME")
    }
    class QueryInterlockStatusCommand {
        +FormatWireMessage() string ("qSTAT")
    }

    IMassHunterCommand <|.. IgnitePlasmaCommand
    IMassHunterCommand <|.. ExtinguishPlasmaCommand
    IMassHunterCommand <|.. StartBatchScriptCommand
    IMassHunterCommand <|.. AppendSampleCommand
    IMassHunterCommand <|.. EmergencyAbortCommand
    IMassHunterCommand <|.. QueryInterlockStatusCommand

    %% Inbound Packets
    class IMassHunterPacket {
        <<interface>>
        +string RawWireText
    }
    class CommandAckResponse {
        +bool IsSuccess
        +string RawWireText
    }
    class MeasurementCompletedEvent {
        +string ResultCsvPath
        +string RawWireText
    }
    class PlasmaTelemetryEvent {
        +double ArgonGasPressureKpa
        +double ChamberVacuumPa
        +double RfGeneratorWatts
        +string RawWireText
    }

    IMassHunterPacket <|.. CommandAckResponse
    IMassHunterPacket <|.. MeasurementCompletedEvent
    IMassHunterPacket <|.. PlasmaTelemetryEvent

    %% Codec
    class MassHunterProtocolCodec {
        +bool SupportsCorrelationId: false
        +TryDecode(ref ReadOnlySequence~byte~, out IMassHunterPacket) bool
        +Encode(IMassHunterCommand, IBufferWriter~byte~) void
        +IsAutonomousMessage(IMassHunterPacket) bool
    }

    %% Driver
    class IIcpmsDriver {
        <<interface>>
        +IgnitePlasmaAsync() Task~bool~
        +ExtinguishPlasmaAsync() Task~bool~
        +StartBatchAsync() Task~BatchRunStatus~
        +AbortBatchAsync() Task~bool~
        +GetBatchStatusAsync() Task~BatchRunStatus~
        +GetPlasmaStateAsync() Task~PlasmaState~
    }
    class MassHunterDeviceDriver {
        -IDeviceSession~IMassHunterPacket~ _commSession
        +IgnitePlasmaAsync() Task~bool~
        +StartBatchAsync() Task~BatchRunStatus~
        +AbortBatchAsync() Task~bool~
    }

    IIcpmsDriver <|.. MassHunterDeviceDriver
    MassHunterDeviceDriver o-- IDeviceSession : Orchestrates reactive session
    MassHunterDeviceDriver ..> IMassHunterCommand : Type-safe command dispatch
    MassHunterProtocolCodec ..> IMassHunterPacket : Decodes inbound frames
```

---

## 3. Data Flowchart

```mermaid
flowchart TD
    A["COM Port Byte Stream (PipeReader Input)"] --> B["MassHunterProtocolCodec.TryDecode (CR '\\r' Framing)"]
    B -->|Incomplete Frame| A
    B -->|Complete Frame| C{"IsAutonomousMessage Evaluation"}
    
    C -->|Measurement Completed CSV Event<br>MeasurementCompletedEvent| D["_commSession.Stream (Business Event Channel)"]
    C -->|Cyclic Telemetry Record<br>PlasmaTelemetryEvent| E["ICommObserver.PeriodicStream (UI Dashboard Ringbuffer)"]
    C -->|Command ACK Response<br>CommandAckResponse| F["Preemptive FIFO Lock Awaiter (RequestAsync TCS)"]
    
    D --> G["Driver fires OnMeasuredCsvDetected -> Ingestion Pipeline"]
    F --> H["Driver awaits RequestAsync -> Transition Complete"]
```

---

## 4. Interaction Timing Diagram

```mermaid
sequenceDiagram
    autonumber
    actor User as Host UI / Automation
    participant Driver as MassHunterDeviceDriver
    participant Session as KableSession (FIFO Lock)
    participant Wire as RS-232C Serial Port
    participant HW as MassHunter / ICP-MS

    Note over User, HW: 1. Serial Port Concurrency Protection (Preemptive FIFO Lock)
    User->>Driver: StartBatchAsync("BATCH-01")
    Driver->>Session: RequestAsync(new StartBatchScriptCommand(...))
    activate Session
    Session->>Wire: TX: oBATCH.CreateNewBatch.script\r
    
    Note over Driver, Session: Concurrent status polling attempts to acquire line
    Driver-->>Session: RequestAsync(new QueryInterlockStatusCommand())
    Note over Session: Uncorrelated instrument: Safely queues behind active transaction!

    Wire->>HW: oBATCH.CreateNewBatch.script\r
    HW-->>Wire: RX: ACK\r
    Wire-->>Session: RX: ACK\r
    Session-->>Driver: CommandAckResponse(IsSuccess = true)
    deactivate Session
    Driver-->>User: Batch Start OK

    Note over Session: Automatically dequeues queued QueryInterlockStatusCommand
    activate Session
    Session->>Wire: TX: qSTAT\r
    HW-->>Wire: RX: #STAT,550,1.2E-3,1500\r
    Wire-->>Session: RX: #STAT,...
    Session-->>Driver: PlasmaTelemetryEvent(550kPa, 1.2mPa, 1500W)
    deactivate Session

    Note over User, HW: 2. Spontaneous Measurement Completion
    HW-->>Wire: RX: $FileName,C:\Data\Run01.csv\r
    Wire-->>Session: Evaluates IsAutonomousMessage == true
    Session-->>Driver: Directly dispatches to Stream -> Fires OnMeasuredCsvDetected!

    Note over User, HW: 3. Out-of-Band Emergency Stop
    User->>Driver: AbortBatchAsync()
    Driver->>Session: SendUrgentAsync(new EmergencyAbortCommand())
    Session->>Wire: TX: oRESUME\r (Bypasses queued transactions directly to wire)
    HW-->>Wire: RX: OK\r
```
