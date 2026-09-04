# LIMS 차세대 통합 통신 아키텍처 및 구현 계획 (INDEX)

> 본 문서는 `CommModule_NVISANA` 레거시 통신 모듈의 부채를 완전 청산하고, **Bedrock의 전송 추상화(하부: Pipelines/ConnectionContext) + RSocket의 상호작용 API(상부: Request/Send/Stream)** 철학을 결합하여 장비 물리 통신(TCP, Serial)과 로컬 프로세스 연동(NamedPipe IPC)을 단일화하는 산업용 표준 통신 도관 라이브러리 **`Kable`**의 종합 색인(INDEX)입니다.

---

## 💎 핵심 설계 4대 확정 원칙 (Architecture Decisions)

1. **하이브리드 트랜잭션 엔진 (Q1 확정)**:
   - 번호표(Correlation ID)가 없는 레거시 단문 ASCII 장비: 내부 비동기 선점형 FIFO 락(`SemaphoreSlim(1, 1)`)으로 응답 뒤섞임 원천 차단.
   - 번호표를 지원하는 고속 IPC/모던 프로토콜: 락 없이 고속 병렬 인터리빙(Pipelining) 지원.
2. **Fail-Fast 단선 안전 정책 (Q2 확정)**:
   - 케이블 탈락 및 단선 감지 시 대기 중인 모든 명령은 즉시 `DeviceDisconnectedException`을 발생시켜 안전 정지 유도 (하드웨어 위험을 초래하는 무분별한 지연 재전송 배제).
3. **철저한 관심사의 분리 (Q3 확정)**:
   - 통신 엔진(`Kable`): 파일 I/O나 디스크 롤링 책임을 일체 배제하고 순수 0-GC 관측성 통지(`OnPacketTrace`)만 발행.
   - 로깅 및 UI 표출: 주입된 로거(`Serilog` 등)와 UI 링버퍼(`ChannelReader`, DropOldest)가 독립적으로 비동기 처리.
4. **레거시 부채 전면 청산 (Q4 확정)**:
   - 불완전한 레거시 호환 어댑터를 두지 않고, 모든 호출부를 `IDeviceSession<T>` 신규 인터페이스로 단일화 및 일괄 전환.

---

## 📚 상세 문서 목차

아래 링크를 통해 세부 주제별 사양서로 바로 이동하실 수 있습니다:

### 1. [01. Architecture Overview (아키텍처 개요)](file:///d:/Johnny/Kable/docs/01_ARCHITECTURE_OVERVIEW.md)
- **핵심 철학**: Bedrock Transport + RSocket Interaction 3단 계층 파이프라인
- **하이브리드 트랜잭션 라우터**: Correlation ID 유무에 따른 자동 직렬화/인터리빙 전환 메커니즘
- **클래스 다이어그램**: 하부 전송(L4), 중간 코덱(Codec), 상부 세션(`KableSession`) 간의 통합 Mermaid 다이어그램

### 2. [02. Core Interfaces Specification (핵심 인터페이스 규격)](file:///d:/Johnny/Kable/docs/02_CORE_INTERFACES.md)
- **`IConnectionContext`**: Bedrock 표준 `PipeReader Input` / `PipeWriter Output` 0-GC 파이프 규격
- **`IProtocolCodec<T>`**: 양방향 0-할당 프레이밍, Correlation ID 판별 및 자발적 알람(`IsAutonomousMessage`) 판별기
- **`IDeviceSession<T>`**: RSocket 스타일 `RequestAsync` / `SendAsync` / `Stream` / `SendUrgentAsync` 규격
- **예외 체계**: `DeviceDisconnectedException`, `DeviceTimeoutException` 등 Fail-Fast 표준 예외

### 3. [03. Observability & Logging Specification (관측성 및 로깅 규격)](file:///d:/Johnny/Kable/docs/03_OBSERVABILITY_LOGGING.md)
- **관심사 분리**: 엔진 내부의 순수 0-GC 발행 vs 외부 로거의 영구 파일 기록
- **트래픽 분리**: `TrafficKind` (주기적 텔레메트리 vs 비정기 제어 명령 vs 비상 알람)
- **`ICommObserver`**: UI 터미널 스크롤 밀림 방지 및 렉 방지(`DropOldest` 링버퍼) 분리 스트림

### 4. [04. Implementation & Directory Layout (독립 라이브러리 구성 계획)](file:///d:/Johnny/Kable/docs/04_IMPLEMENTATION_LAYOUT.md)
- **독립 Git 저장소**: `Kable` 전용 독립 리포지토리 구성, CI/CD 파이프라인 및 단위 테스트
- **순수 네임스페이스**: `Kable.Core/Transports/Codecs/Engine/Exceptions/Observability/Generators`
- **LIMS 및 타 솔루션 연동**: NuGet 패키지(`PackageReference Include="Kable"`) 소비 가이드 및 서비스 등록

### 5. [05. Case Study: ICP-MS Multi-Vendor Integration (ICP-MS 다중 벤더 통합 실사례)](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/INDEX.md)
- **[01. Multi-Vendor Strategy](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/01_MULTI_VENDOR_STRATEGY.md)**: 3단계 표준 확장 절차 및 Agilent vs PerkinElmer 아키텍처 비교표
- **[02. Agilent Protocol Spec](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/02_AGILENT_PROTOCOL_SPEC.md)**: Agilent 7900/8900 ExtDevice RS-232C 바이트 명세표
- **[03. Agilent Architecture & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/03_AGILENT_ARCHITECTURE_DESIGN.md)**: `Icpms.MassHunter` 클래스 다이어그램, 흐름도, FIFO 락 타이밍 차트
- **[04. Agilent Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/04_AGILENT_IMPLEMENTATION_CODE.md)**: `Icpms.MassHunter` 독립 모듈 프로덕션 소스코드 및 DI 등록
- **[05. PerkinElmer Syngistix Spec & Design](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/05_PERKINELMER_SYNGISTIX.md)**: `RemoteSyngistix.cs` 역설계 분석, RPC 명세표, 시퀀스 타이밍 차트
- **[06. PerkinElmer Implementation Code](file:///d:/Johnny/Kable/docs/05_ICP_MS_CASE_STUDY/06_PERKINELMER_IMPLEMENTATION.md)**: `Icpms.PerkinElmer` 독립 모듈 프로덕션 소스코드 및 DI 등록

### 6. [06. Test Engineering & QA Master Plan (테스트 엔지니어링 및 결함 방어 계획)](file:///d:/Johnny/Kable/docs/qa_test_engineering/INDEX.md)
- **[01. Gap Analysis](file:///d:/Johnny/Kable/docs/qa_test_engineering/01_GAP_ANALYSIS.md)**: 현행 75개 테스트의 강점 분석, 미검증 6대 사각지대(Dead Zones) 정밀 식별
- **[02. Codec & Framing Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/02_CODEC_AND_FRAMING_TESTS.md)**: OOM 방어, 1바이트 슬라이딩 윈도우, ArrayPool 대여/반환 무결성
- **[03. Session Concurrency Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/03_SESSION_CONCURRENCY_TESTS.md)**: 200개 동시 요청 FIFO 락 공정성, 타임아웃 지연 패킷 격리, 대규모 단선 Fail-Fast
- **[04. Transport Fault Injection Tests](file:///d:/Johnny/Kable/docs/qa_test_engineering/04_TRANSPORT_FAULT_INJECTION_TESTS.md)**: TCP RST, NamedPipe 프로세스 Crash, SerialPort 케이블 탈락, 배압 제어
- **[05. All New Test Cases Catalog](file:///d:/Johnny/Kable/docs/qa_test_engineering/05_ALL_NEW_TEST_CASES_CATALOG.md)**: 신규 투입 대상 24개 테스트 케이스 전수 카탈로그 매트릭스

