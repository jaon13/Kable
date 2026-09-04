# 05. 신규 테스트 케이스 종합 카탈로그 (All New Test Cases Catalog)

> **문서 상태**: Approved Test Catalog  
> **총 신규 테스트 케이스 수**: 24개 핵심 시나리오 (P0: 8개, P1: 10개, P2: 6개)

---

## 1. 코덱 및 프레이밍 계층 (Codecs & Framing)

| TC ID | 분류 | 우선순위 | 테스트 케이스 명칭 | 핵심 검증 내용 |
| :--- | :--- | :---: | :--- | :--- |
| **TC-COD-101** | 보안/안정성 | **P0** | `Codec_InfiniteStreamWithoutDelimiter_ThrowsOrDiscardsSafely` | 1MB 이상 개행 없는 패킷 유입 시 OOM 방지 및 프레임 상한선 초과 방어 |
| **TC-COD-102** | 프레이밍 | **P1** | `Codec_SingleByteSlidingWindow_ReassemblesCompleteMessage` | 100바이트 메시지를 1바이트 단위 세그먼트로 잘게 쪼갰을 때 완벽 조립 |
| **TC-COD-103** | 엣지케이스 | **P2** | `Codec_ConsecutiveDelimiters_EmitsEmptyFramesWithoutException` | `\r\n\r\n\n\n` 연속 개행 시 인덱스 에러 없이 빈 메시지 연속 디코딩 |
| **TC-COD-104** | 인코딩 | **P1** | `Codec_MultiByteUtf8SplitAcrossSegments_PreservesCharacters` | 3바이트 UTF-8 한글 바이트가 세그먼트 경계에 나뉘어 있을 때 글자 깨짐 없음 |
| **TC-COD-105** | 메모리무결성 | **P0** | `Codec_ArrayPoolRentAndReturn_MaintainsPerfectBalance` | 다중 세그먼트 디코딩 시 `ArrayPool` Rent 횟수와 Return 횟수 1:1 일치 |
| **TC-COD-106** | 확장성 | **P1** | `Codec_BinaryLengthPrefixedHeader_WaitsForFullBody` | 가변 바이너리 헤더의 BodyLength 미달 시 커서 전진 없이 다음 I/O 대기 |

---

## 2. 엔진 및 세션 동시성 계층 (Engine & Session Concurrency)

| TC ID | 분류 | 우선순위 | 테스트 케이스 명칭 | 핵심 검증 내용 |
| :--- | :--- | :---: | :--- | :--- |
| **TC-SES-101** | 동시성/스트레스 | **P0** | `Session_200ConcurrentFifoRequests_MaintainsStrictOrderWithoutDeadlock` | 200개 Task가 동시 `RequestAsync` 호출 시 FIFO 순서 보장 및 데드락 제로 |
| **TC-SES-102** | 결함격리 | **P0** | `Session_LateResponseAfterTimeout_RoutesToStreamWithoutPollutingNextRequest` | 타임아웃 만료 직후 도착한 지연 유령 응답이 다음 요청을 오염시키지 않고 스트림으로 우회 |
| **TC-SES-103** | 우선순위 | **P1** | `Session_SendUrgentAsync_BypassesFifoLockInstantly` | 대용량 FIFO 요청 대기 중에도 `SendUrgentAsync`가 락 점유 없이 즉각 전송 |
| **TC-SES-104** | 결함내성 | **P0** | `Session_AbruptDisconnect_Cancels100PendingRequestsFailFast` | 100개 요청 대기 중 케이블 단선 시 10ms 이내 전원 `DeviceDisconnectedException` |
| **TC-SES-105** | 수명주기 | **P2** | `Session_MultipleStartAndStop_MaintainsIdempotency` | `StartAsync` 10회 연속 호출 및 `StopAsync` 중복 호출 시 단일 소켓 유지 및 안전성 |
| **TC-SES-106** | 동시성 | **P1** | `Session_CorrelationIdCollisionDefense_ThrowsOrReplacesSafely` | 동일한 Correlation ID가 동시 발송되었을 때의 충돌 방어 정책 검증 |
| **TC-SES-107** | 취소처리 | **P1** | `Session_CallerCancellationTokenExpired_CancelsWithoutCorruptingEngine` | 호출자가 전달한 `CancellationToken` 만료 시 파이프라인 무결성 유지 |
| **TC-SES-108** | 관측성 | **P2** | `Session_ObserverQueueOverflow_DropsOldestAndPreservesLatestPacket` | UI 링버퍼 용량 초과 시 오래된 텔레메트리만 드롭되고 최신 패킷 100% 보존 |

---

## 3. 물리 전송 계층 결함 주입 (Transport Fault Injection)

| TC ID | 분류 | 우선순위 | 테스트 케이스 명칭 | 핵심 검증 내용 |
| :--- | :--- | :---: | :--- | :--- |
| **TC-TRN-101** | 소켓결함 | **P0** | `Tcp_ForceResetRstPacket_AbortsConnectionContextInstantly` | LingerState(0)을 통한 TCP RST 유입 시 `DeviceDisconnectedException` 전파 |
| **TC-TRN-102** | 프로세스결함 | **P1** | `NamedPipe_ServerProcessAbruptTermination_DetectsPipeBroken` | NamedPipe 서버 프로세스 강제 Kill 시 클라이언트가 Hang 걸리지 않고 EOF 감지 |
| **TC-TRN-103** | 하드웨어결함 | **P1** | `SerialPort_PhysicalCableRemoval_HandlesBaseStreamDisposed` | 시리얼 통신 중 USB 케이블 분리 시 스레드 풀 블로킹 없는 완벽한 Dispose |
| **TC-TRN-104** | 배압/메모리 | **P1** | `Pipe_BackpressureThresholdExceeded_PausesWriterUntilDrain` | 수신 지연 시 `PipeWriter.FlushAsync()`가 메모리를 무한 증식시키지 않고 대기 |
| **TC-TRN-105** | 성능/GC | **P1** | `Benchmark_10000TelemetryPackets_ZeroGen2GarbageCollection` | 10,000건 스트리밍 패킷 수신 시 Gen2 GC 수집 횟수 0회(Zero-GC) 보증 |
| **TC-TRN-106** | 네트워크 | **P2** | `Tcp_ConnectionTimeoutToNonRoutableIp_ThrowsCleanTimeoutException` | 블랙홀 IP(드롭 게이트웨이) 접속 시 무한 행 방지 및 타임아웃 준수 |

---

## 4. 소스 생성기 및 확장 계층 (Source Generators & Extensions)

| TC ID | 분류 | 우선순위 | 테스트 케이스 명칭 | 핵심 검증 내용 |
| :--- | :--- | :---: | :--- | :--- |
| **TC-GEN-101** | 컴파일검증 | **P1** | `Generator_MissingTemplateProperty_EmitsDiagnosticWarningOrError` | `[DeviceCommand("oCMD.{InvalidProp}")]` 선언 시 컴파일러 진단 발행 검증 |
| **TC-GEN-102** | 기능검증 | **P1** | `Generator_NestedRecordsAndInheritedTypes_GeneratesValidInterface` | 상속 및 중첩 레코드 구조체에서 `IDeviceWireCommand` 정상 생성 |
| **TC-GEN-103** | DI등록 | **P2** | `Builder_MissingCodecOrFactory_ThrowsDescriptiveInvalidOperationException` | `KableClientBuilder` 필수 설정 누락 시 명확한 에러 메시지 검증 |
| **TC-GEN-104** | DI생명주기 | **P2** | `ServiceCollection_AddKableSession_ResolvesCorrectSingletonOrScoped` | Microsoft.Extensions.DependencyInjection 컨테이너 등록 및 수명주기 검증 |
