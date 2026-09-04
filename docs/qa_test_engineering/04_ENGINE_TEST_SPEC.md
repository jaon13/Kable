# 04. Engine Layer Test Specification

> **Target Components**: `Kable.Engine`, `Kable.Engine.Disruptor`  
> **Interfaces**: `IDeviceSession<TMessage>`  
> **Key Implementations**: `KableSession<TMessage>`, `SpscRingBuffer<T>`, `PaddedSequence`  
> **Related Design**: [SYSTEM_DESIGN.md (Section 3)](file:///d:/Johnny/Kable/docs/SYSTEM_DESIGN.md)

---

## 1. 개요 및 계층의 역할

Engine 계층은 하드웨어와의 트랜잭션 라우팅을 총괄하는 핵심 두뇌입니다.
- **Legacy ASCII 장비**: Correlation ID가 없는 하드웨어에 대한 비동기 FIFO 락(`SemaphoreSlim(1,1)`) 직렬화.
- **Modern 프로토콜**: Correlation ID 기반의 Lock-Free 다중화(Multiplexing) 및 비동기 응답 매칭.
- **I/O 및 디스패치 파이프라인**: 0-GC I/O 수신 루프와 전용 디스패치 큐(`_dispatchQueue`) 분리로 하드웨어 I/O 블로킹 방지.
- **Fail-Fast 안전 정책**: 케이블 탈락 시 대기 중인 모든 트랜잭션의 즉각 중단.

---

## 2. 현재 구현된 주요 테스트 현황 (Existing Tests)

| 테스트 ID | 테스트 메서드명 | 검증 내용 |
| :--- | :--- | :--- |
| `TC_SES_01` | `TC_SES_01_RequestAsync_WithCorrelationIdCodec_EnablesOutOfOrderResponses` | Correlation ID 기반 비순차 응답 매칭 |
| `TC_SES_02` | `TC_SES_02_RequestAsync_LateResponseAfterTimeout_DoesNotPolluteNextRequest` | 타임아웃 후 늦게 도착한 유령(Phantom) 응답의 격리 |
| `TC_SES_03` | `TC_SES_03_RequestAsync_SimultaneousAlarmAndResponse_RoutesCorrectly` | 요청 대기 중 긴급 알람 인입 시 알람 채널로 분리 라우팅 |
| `TC_SES_04` | `TC_SES_04_RequestAsync_InvalidResponseTypeCast_ThrowsInvalidCastAndReleasesLock` | 타입 캐스팅 실패 시 FIFO 락 정상 반환 |
| `TC_SES_05` | `TC_SES_05_StartAsync_MultipleCalls_IdempotentAndThreadSafe` | `StartAsync` 다중 호출 멱등성 보장 |
| `TC_SES_06` | `TC_SES_06_Stream_ConsumerAbortsMidway_DoesNotBlockSessionReadLoop` | 스트림 소비자가 중간에 구독을 중단해도 세션 루프 정상 작동 |
| `TC_SES_07` | `TC_SES_07_SendUrgentAsync_UnderFifoContention_BypassesWaitingQueueImmediately` | 긴급 명령(E-STOP)의 FIFO 락 즉시 우회 전송 |
| `TC_SES_08` | `TC_SES_08_OnConnectionClosed_MultipleConcurrentCallers_AllReceiveFailFastException` | 케이블 절단 시 20개 대기 호출자 동시 Fail-Fast 예외 수신 |
| `TC_SES_101` | `TC_SES_101_Session_200ConcurrentFifoRequests_MaintainsStrictOrderWithoutDeadlock` | 100~200개 동시 요청 시 엄격한 순서 보장 및 데드락 방지 |
| `TC_SES_104` | `TC_SES_104_Session_AbruptDisconnect_Cancels100PendingRequestsFailFast` | 50개 대기 요청의 3초 이내 페일패스트 종료 |
| `TC_ENG_01` | `StopAsync_ShouldGracefullyJoinAllInternalTasks_WithoutExceptions` | `StopAsync` 시 2.5초 이내 우아한 태스크 조인 |
| `TC_ENG_02` | `ProducerConsumer_UnderHighThroughputBursts_DeliversAllMessagesInOrder` | 대량 버스트 트래픽 순서 보장 및 무손실 전달 |
| `TC_ENG_SPSC`| `SpscRingBuffer_ConcurrentProducerConsumer_NeverLosesData` | 50,000건 SPSC 무손실 고속 락프리 링버퍼 전송 |

---

## 3. 신규 보강 필요 테스트 케이스 명세 (Required New Test Cases)

### 📌 TC_ENG_201: KableSession_DispatchQueueBackpressure_FullModeWaitSafe
- **목적**: `KableSession` 내부의 `_dispatchQueue`는 용량 10,000의 바운디드 채널입니다. 만약 소비자가 처리하지 못해 10,000건이 가득 찼을 때, 수신 루프(`ReadLoopAsync`)에서 `WriteAsync`를 통해 배압(Backpressure)이 자연스럽게 걸리고 메모리 고갈(OOM) 없이 대기 후 안전하게 드레인되는지 검증.
- **실행 단계**:
  1. 소비자를 시작하지 않고 10,000건을 초과하는 대량의 메시지(10,500건)를 소켓에 연속 주입.
  2. 메모리 점유율 폭증 여부 확인.
  3. 소비자를 시작하여 모든 메시지를 순차적으로 드레인.
- **기대 결과**:
  - 누락 메시지 없이 총 10,500건이 온전히 수신됨.

### 📌 TC_ENG_202: KableSession_Disposed_OperationsThrowObjectDisposedException
- **목적**: `KableSession.DisposeAsync()` 또는 `Dispose()`가 완전히 완료된 이후, 다른 백그라운드 스레드에서 `SendAsync`, `RequestAsync`, `SendUrgentAsync`를 호출할 경우 데드락에 빠지지 않고 즉시 명확한 `ObjectDisposedException` 또는 `DeviceDisconnectedException`을 방출하는지 검증.
- **기대 결과**:
  - 일관된 예외 발생 및 무한 블로킹 배제.

### 📌 TC_ENG_203: KableSession_RequestAsync_CallerCanceledDuringWrite_ReleasesFifoLock
- **목적**: 요청 메시지를 소켓 버퍼로 출력(`FlushAsync`)하는 극히 짧은 순간에 호출자 `CancellationToken`이 취소되었을 때, `_fifoLock` 세마포어가 영구적으로 잠기지 않고 안전하게 `Release()`되어 후속 호출자들이 정상 동작할 수 있는지 스트레스 검증.
- **기대 결과**:
  - 취소된 호출자는 `OperationCanceledException` 수신.
  - 후속 요청은 정상적으로 락을 획득하여 성공.
