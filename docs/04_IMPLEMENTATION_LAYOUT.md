# 04. Implementation & Directory Layout (독립 라이브러리 구성 계획)

> 본 문서는 LIMS 프로젝트 내부 종속성을 0%로 완전히 배제하고, **별도의 독립 Git 저장소(`github.com/.../Kable`)에서 관리되어 NuGet 패키지(`Kable.nupkg`)로 발행되는 순수 최상위 장비 통신 인프라 라이브러리 `Kable`**의 패키징 구조, 네임스페이스 및 LIMS 연동 방식을 정의합니다.

---

## 1. 독립 Git 리포지토리 구성 (`Kable/`)

`Kable`은 독자적인 Git 저장소에서 CI/CD와 단위 테스트를 수행하며, NuGet 피드(NuGet.org 또는 사내 프라이빗 피드)로 배포됩니다:

```
Kable/                                         # [별도 독립 Git 저장소 루트]
│
├── .github/workflows/ci-cd.yml                # 자동 빌드, 테스트, NuGet 패키지 자동 발행
├── Kable.sln                                  # 독립 솔루션 파일
├── README.md                                  # 라이브러리 소개 및 퀵스타트
│
├── src/
│   ├── Kable/                                 # [Kable 메인 코어 라이브러리]
│   │   ├── Kable.csproj                       # NuGet 메인 패키지
│   │   │
│   │   ├── Core/                              # [L4 Transport Abstractions]
│   │   │   ├── IConnectionContext.cs          # PipeReader Input / PipeWriter Output / ConnectionClosed
│   │   │   ├── IConnectionFactory.cs          # 클라이언트 연결 팩토리 (ConnectAsync)
│   │   │   └── IConnectionListener.cs         # 서버 리스너 인터페이스 (AcceptAsync)
│   │   │
│   │   ├── Transports/                        # [Transport Plugins]
│   │   │   ├── TcpConnection.cs               # TCP Active 클라이언트 (L4 KeepAlive 1s/3회 프로브)
│   │   │   ├── TcpListener.cs                 # TCP Passive 서버 리스너 (IConnectionListener)
│   │   │   ├── NamedPipeConnection.cs         # 고속 로컬 프로세스 IPC 클라이언트
│   │   │   └── SerialPortConnection.cs        # RS-232/422/485 물리 COM 포트 (BaudRate, DTR/RTS)
│   │   │
│   │   ├── Codecs/                            # [Zero-Allocation Codecs]
│   │   │   ├── IProtocolCodec.cs             # TryDecode / Encode / SupportsCorrelationId
│   │   │   ├── AsciiLineCodec.cs             # 개행(\n, \r\n) 가변 길이 코덱
│   │   │   ├── LengthPrefixedCodec.cs        # BigEndian 바이너리 헤더 길이 코덱
│   │   │   ├── StxEtxBinaryCodec.cs          # 산업 표준 STX(0x02) ~ ETX(0x03) 코덱
│   │   │   └── JsonIpcCodec.cs               # JSON 직렬화 및 CorrelationId 코덱
│   │   │
│   │   ├── Engine/                            # [Session Engine]
│   │   │   ├── IDeviceSession.cs             # RequestAsync / SendAsync / Stream / SendUrgentAsync
│   │   │   ├── KableSession.cs               # 하이브리드 FIFO 락(SemaphoreSlim) + 인터리빙, Fail-Fast 단선 통지
│   │   │   └── SessionState.cs               # FSM 세션 상태 (Disconnected, Connecting, Ready, Faulted)
│   │   │
│   │   ├── Exceptions/                        # [Fail-Fast Exception Hierarchy]
│   │   │   ├── DeviceDisconnectedException.cs # 단선 즉시 발생 (Fail-Fast)
│   │   │   └── DeviceTimeoutException.cs      # 워치독 타임아웃 격리 예외
│   │   │
│   │   ├── Observability/                     # [Traffic Separator & RingBuffer]
│   │   │   ├── ICommObserver.cs              # PeriodicStream / CommandStream / AlarmStream 분리 인터페이스
│   │   │   ├── PacketTraceRecord.cs          # TrafficKind 및 RTT 지연시간 레코드 (0-GC Memory Slice)
│   │   │   └── CommObserver.cs               # Channel 기반 3단 분리 디스패처 (UI 링버퍼 DropOldest 적용)
│   │   │
│   │   └── Extensions/                        # [DI 등록 유틸리티]
│   │       └── KableServiceExtensions.cs     # services.AddKable() 확장 메서드
│   │
│   └── Kable.Generators/                      # [Kable Roslyn Source Generator 프로젝트]
│       ├── Kable.Generators.csproj            # Roslyn Analyzer & Source Generator 패키지
│       ├── DeviceCommandAttribute.cs         # [DeviceCommand("oPON")] 자동 직렬화 마커
│       ├── SpontaneousEventAttribute.cs      # [SpontaneousEvent("$FileName...")] 자발적 알람 마커
│       ├── DeviceRpcContractAttribute.cs     # [DeviceRpcContract] 선언형 RPC 마커
│       └── ProtocolSourceGenerator.cs        # 컴파일 타임 0-할당 코덱 및 RPC 프록시 생성기
│
└── tests/
    └── Kable.Tests/                           # 단위/통합 테스트 (TDD 검증)
        ├── AsciiLineCodecTests.cs
        ├── KableFifoLockTests.cs
        └── FailFastDisconnectionTests.cs
```

---

## 2. 순수 최상위 네임스페이스 규칙 (Namespace Standards)

`Lims.` 등 특정 비즈니스 도메인 접두어를 100% 배제하여, 외부 프로젝트 어디에 가져다 붙여도 자연스러운 BCL 표준 네임스페이스 체계를 갖춥니다:

| 계층 | 네임스페이스 (Namespace) | 역할 |
| :--- | :--- | :--- |
| **Core** | `Kable.Core` | Bedrock 표준 전송 추상화 (`IConnectionContext`) |
| **Transports** | `Kable.Transports` | TCP, Serial, NamedPipe 물리/논리 전송 구현체 |
| **Codecs** | `Kable.Codecs` | 0-GC 프레이머 및 양방향 직렬화 인터페이스 |
| **Engine** | `Kable.Engine` | RSocket 스타일 상호작용 세션 엔진 (`IDeviceSession`, `KableSession`) |
| **Exceptions**| `Kable.Exceptions`| Fail-Fast 산업용 표준 예외 모델 |
| **Observability**| `Kable.Observability`| 트래픽 종류별 UI 링버퍼 분리 스트림 |
| **Generators**| `Kable.Generators`| Source Generator용 어노테이션 계약 |
| **IoC DI** | `Microsoft.Extensions.DependencyInjection` | .NET 표준 서비스 컬렉션 확장 메서드 |

---

## 3. LIMS 및 외부 프로젝트 연동 방식 (NuGet Package Consumption)

LIMS 및 타 솔루션은 `Kable` 소스코드를 직접 복사하지 않고, **NuGet 패키지 참조(`PackageReference`)**를 통해 우아하게 소비합니다.

### Step 1. LIMS 프로젝트 파일에서 패키지 참조 (`src/Icpms.MassHunter.csproj`)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- 독립 배포된 Kable 패키지만 쏙 받아옴 -->
    <PackageReference Include="Kable" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### Step 2. DI 컨테이너 서비스 등록 (`Program.cs`)
```csharp
using Kable;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 1. Kable 인프라 코어 등록
services.AddKable();

// 2. LIMS 장비 벤더 드라이버 등록 (Kable Session 주입)
services.AddMassHunterDeviceDriver(serialPort: "COM3", baudRate: 9600);
```

### Step 3. 외부 프로젝트(스마트팩토리 PLC, IPC)에서의 재사용 예시
```csharp
using Kable.Core;
using Kable.Engine;
using Kable.Transports;
using Kable.Codecs;

// TCP 소켓으로 PLC에 연결 (개행 프로토콜)
await using var plcSession = new KableSession<string>(
    connectionFactory: new SocketConnectionFactory("192.168.0.10", 502),
    codec: new AsciiLineCodec()
);

await plcSession.StartAsync();
// 단선 시 즉시 Fail-Fast 예외, 평상시 3초 워치독 격리
string ack = await plcSession.RequestAsync<string>("START_MOTOR", TimeSpan.FromSeconds(3));
```
