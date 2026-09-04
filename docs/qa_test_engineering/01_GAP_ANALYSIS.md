# 01. Test Suite Audit & Deep Gap Analysis

> **Document Status**: Approved Baseline Specification  
> **Target Scope**: `src/Kable`, `src/Kable.Generators`, `tests/Kable.Tests`, `tests/Kable.Generators.Tests`  
> **Audit Focus**: Industrial Rigor, Unbounded Allocations, Deadlocks, Physical Hardware Faults  

---

## 1. 현행 테스트 스위트 현황 (Baseline Audit)

현재 Kable 솔루션은 **총 96개의 단위 및 통합 테스트**를 통과하고 있으며, 기본 통신 및 프레이밍 기능에 대해 높은 신뢰성을 보이고 있습니다:
* **Codecs (단편화 및 기본 프레이밍)**: 1바이트 단위 단편화, Multi-segment sequence 디코딩, `MaxFrameSize` 기본 차단
* **Engine (동시성 및 세션)**: 100개 스레드 FIFO 락 순서 보장, 타임아웃 발생 후 지연 응답 격리
* **Transports (장애 주입)**: TCP RST 발생 시 즉각 중단, NamedPipe 크래시 감지, 고빈도 텔레메트리 Gen2 GC 방지
* **Roslyn Generators**: 명령 코드 생성 및 문자열 보간 정밀도

그러나, **하드웨어 필드 현장(Field Instrumentation Environment)**의 복합적인 장애 조건 관점에서 추가적인 **8대 잠재적 결함 지대(Architectural Dead Zones)**가 식별되었습니다.

---

## 2. 새롭게 식별된 8대 결함 지대 (Deep Dead Zones)

### 🔴 GAP 1: 멀티바이트 구분자(`\r\n` 등)의 세그먼트 경계 분할 디코딩 결함
- **위험성**: 단일 바이트 `0x0A`는 잘 처리되나, `\r\n`과 같은 2바이트 구분자 사용 시 `\r`이 첫 번째 세그먼트 끝에 있고 `\n`이 다음 세그먼트 시작에 걸치는 경우 프레임 검출 누락 발생 가능성.
- **검증 대책**: `\r\n` 구분자가 정확히 세그먼트 경계에 걸치는 극한의 슬라이스 테스트 케이스 추가.

### 🔴 GAP 2: 요청 대기 중 CancellationToken 취소 시 FIFO 락 및 세션 상태 복원력
- **위험성**: `RequestAsync` 호출자가 전달한 `CancellationToken`이 타임아웃 전에 외부에서 취소될 때, 내부 `_fifoLock`이 즉시 해제되고 대기 큐(`_currentFifoTcs`)가 깨끗하게 정리되는지 검증 미비.
- **검증 대책**: 취소 발생 직후 다음 FIFO 요청이 락 데드락 없이 정상 전송되는지 검증.

### 🔴 GAP 3: 응답 타입 불일치(`InvalidCastException`) 발생 시 락 해제 보장
- **위험성**: `RequestAsync<TResponse>`에서 장비가 예상과 다른 규격의 응답을 반환하여 타입 변환 예외가 발생할 때, `finally` 블록에서 `_fifoLock`이 정상 해제되어 다음 요청에 지장을 주지 않는지 검증 필요.
- **검증 대책**: 고의로 잘못된 제네릭 타입 요청 시 예외 발생 및 다음 정상 요청 성공 검증.

### 🟡 GAP 4: 복수 구독자에 대한 스트림(`GetStreamAsync`) 채널 독립성
- **위험성**: `KableSession.Stream`에 복수의 백그라운드 워커가 동시에 접근하여 열거(`await foreach`)할 때 데이터 분배 또는 채널 소비 충돌 여부.
- **검증 대책**: 복수 스트림 소비자 환경에서의 메시지 수신 거동 명세화.

### 🟡 GAP 5: TcpListener 중단(`Stop()`) 및 재시작 라이프사이클
- **위험성**: 서버 모드 `TcpConnectionListener`가 실행 중 `Stop()` 또는 `DisposeAsync()` 호출 시 이미 대기 중인 `AcceptAsync()` 태스크의 안전한 취소 및 소켓 바인딩 해제 여부.
- **검증 대책**: 포트 재바인딩 및 수신 대기 루프 종료 안전성 검증.

### 🟡 GAP 6: CommObserver 링버퍼의 다중 스레드 동시 발행 동시성
- **위험성**: 초당 수만 개의 텔레메트리와 알람이 서로 다른 스레드/코어에서 `OnPacketTrace`로 밀려들 때 락 없는 원자적 채널 버퍼 동작 및 드롭 순서 무결성.
- **검증 대책**: 100개 태스크 병렬 발행 시 버퍼 용량(Capacity) 및 최신 데이터 유지 검증.

### 🟡 GAP 7: NamedPipe 연결 시도 중 타임아웃 만료 시 리소스 해제
- **위험성**: 파이프 서버가 존재하지 않을 때 `NamedPipeConnectionFactory`의 지정된 타임아웃 이후 행(Hang) 없이 즉시 예외 방출 및 핸들 누수 방지.
- **검증 대책**: 서버 없는 파이프 연결 시도 시 타임아웃 타이머 정확성 및 메모리 정리 검증.

### 🟡 GAP 8: 대용량 연속 수신 시 AdvanceTo와 파이프 버퍼 배압(Backpressure) 한계
- **위험성**: 코덱에서 디코딩 성공 후 남은 불완전 프레임이 파이프라인의 `Examined` 포인터로 올바르게 갱신되지 않을 경우 파이프 읽기 루프 무한 스핀 발생 가능성.
- **검증 대책**: 불완전 프레임 누적 시 읽기 루프의 정상 블로킹 대기 상태 검증.

---

## 3. 우선순위 매트릭스 (Priority Matrix)

| Priority | Layer | Gap ID | Description | Remediation Test Target |
| :--- | :--- | :--- | :--- | :--- |
| **P0** | Engine | GAP-02 | Caller Cancellation during FIFO Wait | 락 해제 및 후속 요청 즉각 처리 검증 |
| **P0** | Engine | GAP-03 | Cast failure lock leakage | `InvalidCastException` 시 리소스 복구 검증 |
| **P1** | Codecs | GAP-01 | 2-byte delimiter split across segments | `\r\n` 경계 슬라이스 단편화 디코딩 |
| **P1** | Transport | GAP-05 | TcpListener Stop & Abort safety | 수신 대기 소켓 바인딩 안전 해제 |
| **P1** | Transport | GAP-07 | NamedPipe Connect Timeout | 무응답 파이프 연결 타임아웃 방출 |
| **P2** | Observer| GAP-06 | Multi-thread CommObserver burst | 병렬 `OnPacketTrace` 링버퍼 검증 |
| **P2** | Engine | GAP-04 | Stream multi-consumer behavior | 자율 스트림 다중 구독 거동 |
| **P2** | Codecs | GAP-08 | Pipe Examined cursor spin defense | AdvanceTo 커서 진행 정확성 |
