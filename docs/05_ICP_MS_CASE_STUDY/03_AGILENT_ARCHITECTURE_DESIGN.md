# 03. Agilent Architecture & Design (애질런트 아키텍처 및 설계)

> 본 문서는 Agilent MassHunter 드라이버 모듈의 디렉터리 구성표, 클래스 다이어그램, 데이터 흐름도 및 타이밍 차트를 정의합니다.

---

## 1. 독립 모듈 디렉터리 레이아웃 (Module Project Layout)

본 드라이버는 다른 장비와 섞이지 않는 **독립 프로젝트 모듈(`Icpms.MassHunter`)**로 패키징되며, `CONVENTIONS.md`에 의거하여 **단일 파일 300줄 이내**로 완벽히 분리됩니다:

```
src/Icpms.MassHunter/                        # [루트 프로젝트 모듈]
│
├── Icpms.MassHunter.csproj                  # .NET 10 클래스 라이브러리
│
├── Protocol/                                # [프로토콜 레이어: 네임스페이스 Icpms.MassHunter.Protocol]
│   ├── MassHunterCommands.cs                # [DeviceCommand] 초간결 송신 명령 선언
│   ├── MassHunterPackets.cs                 # [SpontaneousEvent] 수신 패킷 & 이벤트 모델
│   └── MassHunterProtocolCodec.g.cs         # 컴파일 타임 자동 생성된 0-할당 코덱
│
├── Driver/                                  # [비즈니스 드라이버: 네임스페이스 Icpms.MassHunter]
│   ├── IIcpmsDriver.cs                      # LIMS 공통 비즈니스 인터페이스 계약
│   ├── MassHunterDeviceDriver.cs            # FSM 상태 제어, 워치독 격리 및 스트림 디스패처
│   └── MassHunterDriverOptions.cs           # 포트명, 보드레이트, 기본 타임아웃 옵션 모델
│
└── Extensions/                              # [IoC 등록: 네임스페이스 Microsoft.Extensions.DependencyInjection]
    └── MassHunterServiceExtensions.cs       # AddMassHunterDeviceDriver 확장 메서드
```



---

## 2. 클래스 다이어그램 (Class Diagram)

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
    MassHunterDeviceDriver o-- IDeviceSession : 비동기 세션 제어
    MassHunterDeviceDriver ..> IMassHunterCommand : 타입 안전 명령 송신
    MassHunterProtocolCodec ..> IMassHunterPacket : 수신 패킷 디코딩
```

---

## 3. 데이터 흐름도 (Data Flowchart)

```mermaid
flowchart TD
    A["COM 포트 바이트 스트림 (PipeReader Input)"] --> B["MassHunterProtocolCodec.TryDecode (CR '\\r' 프레이밍)"]
    B -->|프레임 미완성| A
    B -->|프레임 완성| C{"IsAutonomousMessage 판별"}
    
    C -->|측정 완료 CSV 이벤트<br>MeasurementCompletedEvent| D["_commSession.Stream (자발적 비즈니스 이벤트)"]
    C -->|주기 계측 텔레메트리<br>PlasmaTelemetryEvent| E["ICommObserver.PeriodicStream (UI 대시보드 링버퍼)"]
    C -->|명령 확인 응답<br>CommandAckResponse| F["선점형 FIFO 락 대기자 (RequestAsync TCS 완료)"]
    
    D --> G["드라이버 OnMeasuredCsvDetected 이벤트 발생 -> LIMS CoA 파이프라인"]
    F --> H["드라이버 await RequestAsync 리턴 -> 상태 전이 완료"]
```

---

## 4. 상호작용 타이밍 차트 (Timing Diagram)

```mermaid
sequenceDiagram
    autonumber
    actor User as LIMS UI (시험자)
    participant Driver as MassHunterDeviceDriver
    participant Session as IndustrialDeviceSession (FIFO Lock)
    participant Wire as RS-232C Wire
    participant HW as MassHunter / ICP-MS

    Note over User, HW: 1. 단일 COM 포트 내 동시성 제어 (선점형 FIFO 락)
    User->>Driver: StartBatchAsync("BATCH-01")
    Driver->>Session: RequestAsync(new StartBatchScriptCommand(...))
    activate Session
    Session->>Wire: TX: oBATCH.CreateNewBatch.script\r
    
    Note over Driver, Session: 백그라운드 모니터링 루프가 동시에 상태 조회 시도
    Driver-->>Session: RequestAsync(new QueryInterlockStatusCommand())
    Note over Session: 번호표 없는 하드웨어: 안전하게 FIFO 대기열에 줄서기!

    Wire->>HW: oBATCH.CreateNewBatch.script\r
    HW-->>Wire: RX: ACK\r
    Wire-->>Session: RX: ACK\r
    Session-->>Driver: CommandAckResponse(IsSuccess = true)
    deactivate Session
    Driver-->>User: 배치 시작 성공!

    Note over Session: 대기열에 있던 QueryInterlockStatusCommand 자동 실행
    activate Session
    Session->>Wire: TX: qSTAT\r
    HW-->>Wire: RX: #STAT,550,1.2E-3,1500\r
    Wire-->>Session: RX: #STAT,...
    Session-->>Driver: PlasmaTelemetryEvent(550kPa, 1.2mPa, 1500W)
    deactivate Session

    Note over User, HW: 2. 계측 완료 시 자발적(Spontaneous) CSV 수신
    HW-->>Wire: RX: $FileName,C:\Data\Run01.csv\r
    Wire-->>Session: IsAutonomousMessage == true 판별
    Session-->>Driver: Stream 이벤트 직결 -> OnMeasuredCsvDetected 통지!

    Note over User, HW: 3. 비상 상황 긴급 중단 (OOB 즉시 송신)
    User->>Driver: AbortBatchAsync()
    Driver->>Session: SendUrgentAsync(new EmergencyAbortCommand())
    Session->>Wire: TX: oRESUME\r (대기 큐 무시하고 와이어 직접 주입)
    HW-->>Wire: RX: OK\r
```
