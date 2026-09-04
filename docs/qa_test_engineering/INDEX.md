# 🧪 Kable QA Test Engineering & Specification Index

> **Document Status**: Production Quality Test Architecture Index  
> **Target Frameworks**: .NET 10.0, .NET 8.0 (LTS), .NET Standard 2.0  
> **Related Documents**: [PROJECT_SPEC.md](file:///d:/Johnny/Kable/docs/PROJECT_SPEC.md), [SYSTEM_DESIGN.md](file:///d:/Johnny/Kable/docs/SYSTEM_DESIGN.md), [CONVENTIONS.md](file:///d:/Johnny/Kable/docs/CONVENTIONS.md)

---

## 📌 개요 및 목적

본 문서는 **Kable (고성능 리액티브 하드웨어 통신 엔진)**의 전체 테스트 커버리지 현황, 기능 영역별(Layer별) 검증 상태, 그리고 **잠재적 결함 방지를 위해 반드시 보강되어야 할 핵심 테스트 케이스(Gap Analysis)**를 분석하고 기능 단위별로 상세화한 마스터 테스트 명세서입니다.

현재 Kable 솔루션은 **총 110개의 자동화 테스트 (Kable.Tests: 105개, Kable.Generators.Tests: 5개)**가 빌드 및 CI 환경에서 100% Pass 상태로 가동 중입니다.  
세계 최고의 테스트 엔지니어 관점에서 과도하고 불필요한 테스트(Mock 남용, 단순 Getter/Setter 테스트 등)를 배제하고, **산업용 하드웨어 통신 환경에서 장비 파손, 스레드 고사, 데드락, 메모리 누수를 유발할 수 있는 실질적인 경계/장애 조건 위주로 기능 단위별 테스트 명세**를 정의합니다.

---

## 🗂️ 기능 단위별 테스트 명세 구조

| 번호 | 문서명 | 대상 기능 영역 | 주요 검증 영역 및 목적 | 상태 |
| :---: | :--- | :--- | :--- | :---: |
| **01** | [01_GAP_ANALYSIS.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/01_GAP_ANALYSIS.md) | 전체 시스템 (System-wide) | 현재 110개 테스트 스위트 커버리지 현황 및 미검증 취약점 심층 분석 | **완료** |
| **02** | [02_TRANSPORT_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/02_TRANSPORT_TEST_SPEC.md) | Layer 1: Transports | TCP, NamedPipe, SerialPort, Listener, Out-of-Process IPC 통신 신뢰성 | **완료** |
| **03** | [03_CODEC_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/03_CODEC_TEST_SPEC.md) | Layer 2: Codecs | AsciiLineCodec, 다중 세그먼트 파편화, 제어문자 경계, 바이너리 프레이밍 | **완료** |
| **04** | [04_ENGINE_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/04_ENGINE_TEST_SPEC.md) | Layer 3: Engine | Hybrid Routing, FIFO 동시성, Backpressure, 링버퍼, OOB 긴급 명령 | **완료** |
| **05** | [05_SOURCE_GENERATOR_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/05_SOURCE_GENERATOR_TEST_SPEC.md) | Roslyn Generators | Roslyn 컴파일 타임 구문 분석, 이스케이프 포맷, 특수 타입 직렬화 | **완료** |
| **06** | [06_OBSERVABILITY_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/06_OBSERVABILITY_TEST_SPEC.md) | Observability & Logging | LogLevel 표준 매트릭스, CATCH 블록 원인 추적, 외부 로거 무의존 브릿지 | **완료** |
| **07** | [07_OBSERVABILITY_CASES_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/07_OBSERVABILITY_CASES_TEST_SPEC.md) | Observability Detailed Cases | 채널 격리 신뢰성, 정상 정지 시 오경보 방지, Tx Flush 오류 전파 상세 테스트 케이스 | **신규 완료** |

---

## 🎯 테스트 엔지니어링 4대 핵심 원칙 (Core Principles)

1. **실제 물리적 하드웨어 거동 모사 (Realistic Hardware Emulation)**
   - 단순 단위 Mocking에 의존하지 않고, 실제 소켓 TCP 리셋(RST), 명명된 파이프 서버 크래시, USB 시리얼 케이블 강제 단선 시나리오를 주입하여 `DeviceDisconnectedException` 페일패스트 즉시 전파 여부를 검증합니다.
2. **0-Allocation & 메모리 안전성 (Zero-GC & OOM Defense)**
   - `ReadOnlySequence<byte>`가 세그먼트 경계(1바이트 단위, 멀티 세그먼트)로 조각나거나 악의적인 무한 바이트 스트림이 유입될 때 `MaxFrameSize` 가드가 OOM(Out of Memory)을 사전에 차단하고 `ArrayPool` 대여 버퍼를 100% 반환하는지 보증합니다.
3. **스레드 분리 및 비동기 파이프라인 안전성 (Threading & Lifecycle)**
   - I/O 루프와 디스패치 루프가 분리된 파이프라인에서 버스트 트래픽 발생 시 I/O 스레드가 블로킹되지 않고, 세션 종료(`StopAsync`/`DisposeAsync`) 시 잔여 작업이 데드락 없이 최대 2초 이내 우아하게 종료(Graceful Join)되는지 검증합니다.
4. **과도한 중복 테스트 지양 (No Redundant/Trivial Tests)**
   - 내부 프라이빗 필드 상태 확인이나 불필요한 추상화 테스트는 배제하고, 공용 인터페이스 계약(`IConnectionContext`, `IProtocolCodec`, `IDeviceSession`)과 예외 흐름 중심의 가치 높은 테스트 케이스에 집중합니다.
5. **하드코딩 배제 및 동적 격리 원칙 (Zero Hardcoding & Dynamic Isolation)**
   - COM 포트명(`COM99`), TCP 포트 번호(`5000`), NamedPipe 이름(`"my_pipe"`) 등의 고정값 하드코딩을 전면 배제합니다.
   - 포트는 OS 자동 할당(`new TcpListener(IPAddress.Loopback, 0)`), 파이프명은 고유 GUID(`$"pipe_{Guid.NewGuid():N}"`), 시리얼 포트는 `SerialPort.GetPortNames()` 동적 스캔을 통해 병렬 테스트 간 충돌과 실행 환경 종속성(Flaky Tests)을 원천 차단합니다.
