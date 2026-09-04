# 05. All Test Cases Master Catalog (Enhanced)

> **Document Status**: Approved Master Catalog (Version 2.0)  
> **Total Test Scenarios**: 32 Target Scenarios (P0: 10, P1: 14, P2: 8) across 5 domain layers.  

---

## 1. Codecs & Framing Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-COD-101** | Security / Safety | **P0** | `Codec_InfiniteStreamWithoutDelimiter_ThrowsProtocolViolationException` | 구분자 없는 64KB+ 유입 시 `ProtocolViolationException` 방출 및 OOM 방지 |
| **TC-COD-102** | Framing | **P1** | `Codec_SingleByteSlidingWindow_ReassemblesCompleteMessage` | 1바이트 슬라이딩 윈도우 단편화 수신 시 온전한 문자열 복원 |
| **TC-COD-103** | Edge Case | **P2** | `Codec_ConsecutiveDelimiters_EmitsEmptyFramesWithoutException` | `\r\n\r\n` 연속 구분자 수신 시 빈 메시지 순차 처리 |
| **TC-COD-104** | Encoding | **P1** | `Codec_MultiByteUtf8SplitAcrossSegments_PreservesCharacters` | 세그먼트 경계에 걸친 멀티바이트 UTF-8 문자 깨짐 없이 디코딩 |
| **TC-COD-105** | Memory Integrity | **P0** | `Codec_ArrayPoolRentAndReturn_MaintainsPerfectBalance` | 멀티세그먼트 디코딩 시 `ArrayPool<byte>` 대여/반환 균형 유지 |
| **TC-COD-106** | Extensibility | **P1** | `Codec_BinaryLengthPrefixedHeader_WaitsForFullBody` | 가변 길이 바이너리 헤더 프레이밍 처리 검증 |
| **TC-COD-107** | [NEW] Boundary | **P0** | `Codec_TwoByteDelimiterSplitAcrossSegments_DecodesCleanly` | `\r\n` 2바이트 구분자가 세그먼트 경계에 걸칠 때 완벽 디코딩 |
| **TC-COD-108** | [NEW] Pipeline Cursor| **P1** | `Codec_IncompleteFrame_PreservesBufferCursorAndExamined` | 불완전 프레임 시 커서 미전진(`examined`만 전진)으로 스핀 방지 |

---

## 2. Engine & Session Concurrency Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-SES-101** | Concurrency / Stress | **P0** | `Session_200ConcurrentFifoRequests_MaintainsStrictOrderWithoutDeadlock` | 100+ 동시 작업에 대한 FIFO 락 공정성 및 1:1 응답 매칭 |
| **TC-SES-102** | Fault Isolation | **P0** | `Session_LateResponseAfterTimeout_RoutesToStreamWithoutPollutingNextRequest` | 타임아웃 이후 지연 응답의 `Stream` 자동 우회 및 차기 요청 오염 방지 |
| **TC-SES-103** | Priority | **P1** | `Session_SendUrgentAsync_BypassesFifoLockInstantly` | FIFO 락 획득 중 긴급 명령의 Out-Of-Band 즉시 전송 |
| **TC-SES-104** | Fault Tolerance | **P0** | `Session_AbruptDisconnect_Cancels100PendingRequestsFailFast` | 하드웨어 단선 시 대기 중인 모든 요청 밀리초 내 Fail-Fast 취소 |
| **TC-SES-105** | Lifecycle | **P2** | `Session_MultipleStartAndStop_MaintainsIdempotency` | `StartAsync`/`StopAsync` 다중 호출 시 멱등성 및 소켓 누수 방지 |
| **TC-SES-106** | Concurrency | **P1** | `Session_CorrelationIdCollisionDefense_ThrowsOrReplacesSafely` | 상관 ID 중복 발생 시의 안전한 트랜잭션 격리 |
| **TC-SES-107** | Cancellation | **P1** | `Session_CallerCancellationTokenExpired_CancelsWithoutCorruptingEngine` | 호출자 토큰 취소 시 내부 세션 상태 유지 |
| **TC-SES-108** | Observability | **P2** | `Session_ObserverQueueOverflow_DropsOldestAndPreservesLatestPacket` | 링버퍼 오버플로우 시 최신 텔레메트리 보존 및 구형 패킷 드롭 |
| **TC-SES-109** | [NEW] Lock Resilience| **P0** | `Session_CallerCancellation_ReleasesFifoLockImmediatelyForNextCaller` | 호출자 토큰 취소 후 즉시 다음 호출자가 FIFO 락 획득 가능 검증 |
| **TC-SES-110** | [NEW] Exception Safety| **P1** | `Session_InvalidCastException_ReleasesFifoLockSafely` | 반환 타입 변환 실패 시에도 세션 락 정상 해제 및 후속 요청 성공 |
| **TC-SES-111** | [NEW] Stream Channel| **P2** | `Session_DisconnectedState_RequestAsyncThrowsImmediately` | 미연결 상태에서 요청 시 지연 없이 즉시 `DeviceDisconnectedException` |

---

## 3. Transport Fault Injection Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-TRN-101** | Socket Fault | **P0** | `Tcp_ForceResetRstPacket_AbortsConnectionContextInstantly` | TCP RST 패킷 주입 시 파이프 리더 즉시 종료 및 세션 단절 통보 |
| **TC-TRN-102** | Process Fault | **P1** | `NamedPipe_ServerProcessAbruptTermination_DetectsPipeBroken` | NamedPipe 서버 크래시 시 EOF 즉시 감지 및 세션 정리 |
| **TC-TRN-103** | Hardware Fault | **P1** | `SerialPort_PhysicalCableRemoval_HandlesBaseStreamDisposed` | 시리얼 케이블 강제 분리 시 안전한 비동기 리소스 해제 |
| **TC-TRN-104** | Backpressure | **P1** | `Pipe_BackpressureThresholdExceeded_PausesWriterUntilDrain` | 리더 지연 시 500+ 프레임에 대한 파이프라인 백프레셔 및 복구 |
| **TC-TRN-105** | Performance / GC | **P1** | `Benchmark_10000TelemetryPackets_ZeroGen2GarbageCollection` | 10,000건 패킷 수신 시 0 Gen2 GC 발생 검증 |
| **TC-TRN-106** | Network | **P2** | `Tcp_ConnectionTimeoutToNonRoutableIp_ThrowsCleanTimeoutException` | 접속 불가능 IP 연결 시도 시 타임아웃 준수 및 정상 종료 |
| **TC-TRN-107** | [NEW] Listener Life | **P1** | `TcpListener_StopAndRestart_RebindsPortWithoutSocketException` | 리스너 `Stop()` 후 동일 포트 즉시 재바인딩 및 수신 대기 루프 종료 |
| **TC-TRN-108** | [NEW] Pipe Timeout | **P1** | `NamedPipe_NonExistentServer_ConnectAsyncTimesOutCleanly` | 미기동 파이프 접속 시 타임아웃 만료 및 핸들 누수 방지 |

---

## 4. Observability & Generator Layer

| TC ID | Category | Priority | Test Case Name | Core Assertion / Objective |
| :--- | :--- | :---: | :--- | :--- |
| **TC-OBS-101** | [NEW] Multi-Thread | **P1** | `CommObserver_MultiThreadedBursts_MaintainsZeroLossInCommands` | 다중 스레드 병렬 `OnPacketTrace` 호출 시 링버퍼 무결성 유지 |
| **TC-GEN-101** | Diagnostics | **P1** | `Generator_MissingTemplateProperty_EmitsDiagnosticWarningOrError` | 템플릿 프로퍼티 누락 시 컴파일러 진단 경고 방출 |
| **TC-GEN-102** | Functionality | **P1** | `Generator_MultiParamRecords_InterpolatesAllParametersCorrectly` | 다중 매개변수 레코드 구조체 전송 포맷 100% 정밀 보간 |
| **TC-GEN-103** | DI Validation | **P2** | `Builder_MissingCodecOrFactory_ThrowsDescriptiveInvalidOperationException` | 빌더 컴포넌트 미설정 시 설명적인 `InvalidOperationException` 방출 |
| **TC-GEN-104** | DI Lifecycle | **P2** | `ServiceCollection_AddKableSession_ResolvesCorrectSingletonOrScoped` | DI 컨테이너에서 세션 싱글톤 라이프사이클 정상 등록 검증 |
