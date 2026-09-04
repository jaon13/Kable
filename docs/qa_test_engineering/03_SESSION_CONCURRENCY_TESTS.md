# 03. 세션 동시성 및 복원력 테스트 스펙 (Session Concurrency & Resilience Tests)

> **문서 상태**: Approved Technical Specification  
> **대상 모듈**: `Kable.Engine`, `KableSession<T>`, `IDeviceSession<T>`

---

## 1. 개요 및 테스트 목표

`KableSession`은 RSocket 상호작용 스타일을 기반으로 하여, **비동기 단일/병렬 요청(`RequestAsync`)**, **일방향 송신(`SendAsync`)**, **자율 알람 스트리밍(`Stream`)**, 그리고 **비상 우선 송신(`SendUrgentAsync`)**을 통합 관리하는 엔진 코어입니다.

본 스펙은 수많은 스레드가 동시에 요청을 발행하거나, 타임아웃과 응답이 나노초 단위로 경합하거나, 통신 두절(Fail-Fast)이 발생하는 극한 상황에서 세션의 상태 머신이 결코 멈추거나(Deadlock) 데이터가 오염되지 않음을 입증합니다.

---

## 2. 상세 테스트 케이스 정의

### TC-SES-01: 고빈도(High Concurrency) FIFO 트랜잭션 락 공정성 및 무결성
- **우선순위**: P0
- **테스트 목적**: Correlation ID가 없는 장비(FIFO 모드)에 200개의 스레드가 동시에 `RequestAsync`를 호출할 때, 응답이 뒤섞이지 않고 요청된 순서대로 1:1 매칭되는지 검증.
- **테스트 절차**:
  1. 200개 Task 생성 및 `session.RequestAsync($"CMD_{i}", timeout)` 일제히 호출.
  2. 서버 측 Mock 루프에서 들어온 커맨드의 번호를 읽어 `RESP_{i}`를 순서대로 응답.
  3. 모든 Task 완료 대기(`Task.WhenAll`).
- **기대 결과**:
  - 모든 호출자가 자신의 커맨드 번호에 맞는 응답을 수령함 (`res == $"RESP_{i}"`).
  - `Deadlock` 없이 전원 성공 완료.

### TC-SES-02: 타임아웃 만료 직후 도착한 지연 응답(Phantom Packet) 격리
- **우선순위**: P0
- **테스트 목적**:
  - 요청 A가 100ms 타임아웃으로 만료되어 `DeviceTimeoutException`을 발생시킨 직후(105ms), 하드웨어에서 응답 A가 뒤늦게 도착했을 때.
  - 이 지연 응답 A가 **다음 요청 B의 응답으로 잘못 소비되지 않고, 일반 이벤트 스트림(`Stream`)으로 안전하게 우회 격리**되는지 검증.
- **기대 결과**:
  - 요청 B는 자신의 정상 응답 B를 기다려 수령함.
  - 지연 응답 A는 `session.Stream`에 수신됨.

### TC-SES-03: 비상 우선 송신(SendUrgentAsync)의 OOB(Out-of-band) 선점 보증
- **우선순위**: P1
- **테스트 목적**: 장시간 소요되는 FIFO `RequestAsync`가 진행 중일 때, 긴급 정지(`SendUrgentAsync("EMERGENCY_STOP")`)를 호출하면 FIFO 락에 블로킹되지 않고 즉각 소켓/파이프로 전송되는지 검증.
- **기대 결과**:
  - `_fifoLock` 점유 상태와 관계없이 `SendUrgentAsync`가 지체 없이 완료됨.
  - 원격 수신측 파이프에 비상 정지 패킷이 선행 도착함.

### TC-SES-04: 하드웨어 단선 시 수백 개 동시 요청의 Fail-Fast 집단 취소
- **우선순위**: P0
- **테스트 목적**: 대기 중인 `RequestAsync` 100개가 존재하는 상황에서 물리 링크가 단선(`OnConnectionClosed()`)되었을 때, 타임아웃을 기다리지 않고 즉각(10ms 이내) 전원 `DeviceDisconnectedException`을 발생시키는지 검증.
- **기대 결과**:
  - 100개 요청 전원이 타임아웃 만료 전에 즉각적인 예외를 수신.
  - `session.IsConnected`가 즉시 `false`로 전이됨.
  - 메모리 상의 `_pendingRequests` 컬렉션이 완전히 클리어됨.

### TC-SES-05: 세션 수명주기 멱등성 (Idempotent Lifecycle)
- **우선순위**: P2
- **테스트 목적**:
  - 이미 연결된 세션에 `StartAsync()` 10회 연속 호출 시 단일 커넥션 유지.
  - `StopAsync()` 및 `DisposeAsync()`를 여러 스레드에서 무작위 순서로 호출할 때 `ObjectDisposedException`이나 널 참조 크래시가 발생하지 않는지 검증.
- **기대 결과**:
  - 내부 `Interlocked` 플래그에 의해 모든 수명주기 전이가 멱등성을 보장함.
