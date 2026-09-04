# 07. Observability Test Cases Detailed Specification

> **Target Components**: `Kable.Core`, `Kable.Observability`, `Kable.Engine`  
> **Key Contracts**: `LogLevel`, `PacketTraceRecord`, `ICommObserver`, `CommObserver`, `KableSession<T>`  
> **Related Specifications**: [06_OBSERVABILITY_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/06_OBSERVABILITY_TEST_SPEC.md), [03_OBSERVABILITY_LOGGING.md](file:///d:/Johnny/Kable/docs/03_OBSERVABILITY_LOGGING.md)  
> **Author**: World-Class Test & QA Architect  

---

## 1. 개요 및 엔지니어링 철학

본 문서는 [06_OBSERVABILITY_TEST_SPEC.md](file:///d:/Johnny/Kable/docs/qa_test_engineering/06_OBSERVABILITY_TEST_SPEC.md)에 기술된 설계를 기반으로, `Kable` 통신 엔진의 관측성(Observability) 신뢰성을 보증하기 위한 구체적인 테스트 케이스를 정의합니다.

### 🎯 테스트 엔지니어링 3대 원칙
1. **필수 엣지 케이스 엄격 검증 (Critical Edge Verification)**:
   - 고주파 텔레메트리 포화 시 긴급 알람 격리, 정상 종료 시 오경보 방지, I/O 플러시 실패 시 Fail-Fast 연계 등 실제 현장 장애 상황을 집중 검증합니다.
2. **과도한/불필요한 중복 테스트 배제 (No Redundant/Trivial Tests)**:
   - C# 컴파일러가 보증하는 단순 필드 Getter/Setter나 BCL `System.Threading.Channels` 자체의 락 메커니즘을 테스트하는 것은 노이즈이므로 배제합니다.
3. **0-GC & 비차단(Non-blocking) 계약 보증**:
   - 로깅/추적 경로가 고속 I/O 통신 파이프라인에 GC 압박이나 디스크 쓰기 블로킹을 전파하지 않음을 보증합니다.

---

## 2. 기존 테스트 스위트 검증 현황 (Baseline)

현재 솔루션에는 총 4개의 관측성 자동화 테스트가 작성되어 100% 통과(Pass) 상태입니다:

| 테스트 ID | 테스트 메서드명 | 검증 내용 | 상태 |
| :--- | :--- | :--- | :---: |
| `TC_OBS_01` | `CommObserver_OverCapacity_DropsOldestAndMaintainsLatestTelemetry` | 주기적 스트림 용량 초과 시 오래된 텔레메트리 자동 드롭 | **Pass** |
| `TC_OBS_02` | `KableSession_WithCommObserver_RoutesAlarmsAndCommandsToObserver` | 자율 알람 및 커맨드 응답의 관측자 정상 전달 | **Pass** |
| `TC_OBS_101` | `TC_OBS_101_CommObserver_MultiThreadedBursts_MaintainsZeroLossInCommands` | 50스레드 동시 버스트 기록 시 커맨드 스트림 무손실 보장 | **Pass** |
| `TC_OBS_102` | `TC_OBS_102_KableSession_LogLevelClassification_EmitsDebugForCommands_AndCriticalForUrgent` | `SendAsync`(Debug)와 `SendUrgentAsync`(Critical) 레벨 분류 검증 | **Pass** |
| `TC_OBS_103` | `TC_OBS_103_KableSession_TimeoutAndStreamCrash_EmitsWarningAndErrorLogLevels` | 타임아웃(Warning) 및 수신 스트림 강제 크래시(Error) 검증 | **Pass** |
| `TC_OBS_104` | `TC_OBS_104_KableSession_DispatchLoopException_LogsErrorAndDoesNotHangEngine` | 디스패치 루프 내 파싱 예외 발생 시 Error 발행 및 무음 정지 방지 | **Pass** |

---

## 3. 신규 보강 필요 테스트 케이스 명세 (Required Must-Have Cases)

### 📌 TC_OBS_201: CommObserver_TelemetryBufferSaturation_MaintainsZeroLossInAlarmAndCommandStreams
- **목적**:
  초고주파 센서 텔레메트리(`PeriodicTelemetry`)가 대량으로 유입되어 텔레메트리 링버퍼가 포화 및 패킷 드롭(`DropOldest`) 상태에 진입하더라도, **독립된 알람 스트림(`AlarmStream`)과 커맨드 스트림(`CommandStream`)은 전혀 영향을 받지 않고 100% 무손실로 즉시 전달되는지 채널 격리 신뢰성을 검증**합니다.
- **사전 조건**:
  - 버퍼 용량이 10으로 작게 설정된 `CommObserver` 인스턴스 생성.
- **실행 단계**:
  ```csharp
  // 1. 버퍼 용량 10인 CommObserver 구성
  var observer = new CommObserver(bufferCapacity: 10);

  // 2. 용량을 초과하는 100건의 주기적 텔레메트리 연속 주입 (오버플로 발생 유도)
  for (int i = 0; i < 100; i++)
  {
      observer.OnPacketTrace(new PacketTraceRecord(
          DateTime.UtcNow, PacketDirection.Rx, TrafficKind.PeriodicTelemetry,
          "SENSOR_STREAM", ReadOnlyMemory<byte>.Empty, $"DATA_{i}", TimeSpan.Zero, LogLevel.Trace));
  }

  // 3. 단 1건의 긴급 알람 및 1건의 커맨드 패킷 주입
  observer.OnPacketTrace(new PacketTraceRecord(
      DateTime.UtcNow, PacketDirection.Rx, TrafficKind.SpontaneousAlarm,
      "CRITICAL_INTERLOCK", ReadOnlyMemory<byte>.Empty, "ALARM_FIRE_DETECTED", TimeSpan.Zero, LogLevel.Critical));

  observer.OnPacketTrace(new PacketTraceRecord(
      DateTime.UtcNow, PacketDirection.Tx, TrafficKind.AperiodicCommand,
      "CMD_SHUTDOWN", ReadOnlyMemory<byte>.Empty, "STOP_ALL", TimeSpan.Zero, LogLevel.Debug));

  // 4. 검증: PeriodicStream은 최신 10건만 유지되고 드롭 발생 확인
  int telemetryCount = 0;
  while (observer.PeriodicStream.TryRead(out _)) telemetryCount++;
  telemetryCount.Should().Be(10);

  // 5. 검증: AlarmStream과 CommandStream은 유실 없이 즉시 드레인되어야 함
  observer.AlarmStream.TryRead(out var alarm).Should().BeTrue();
  alarm.ParsedText.Should().Be("ALARM_FIRE_DETECTED");
  alarm.Level.Should().Be(LogLevel.Critical);

  observer.CommandStream.TryRead(out var cmd).Should().BeTrue();
  cmd.ParsedText.Should().Be("STOP_ALL");
  ```
- **기대 결과**:
  - `PeriodicStream`의 드롭 동작이 `AlarmStream` 및 `CommandStream` 채널에 일체 간섭하지 않음.

---

### 📌 TC_OBS_202: KableSession_CleanShutdown_DoesNotEmitFalseErrorAlerts
- **목적**:
  장비 세션의 정상 종료(`StopAsync()` 또는 `DisposeAsync()`) 시 수신 루프(`ReadLoopAsync`) 및 디스패치 루프(`DispatchLoopAsync`)에서 취소 토큰에 의한 `OperationCanceledException`이 정상 발생합니다. 이것은 정상적인 협력적 종료(Cooperative Cancellation) 흐름이므로, **모니터링 알람 시스템에 불필요한 장애 오경보(`LogLevel.Error` 또는 `READ_LOOP_FAULT`)가 발행되지 않아야 함**을 검증합니다 (신호 대 잡음비 SNR 보증).
- **사전 조건**:
  - `TestMemoryConnectionFactory`와 `AsciiLineCodec`을 장착한 `KableSession` 시작.
- **실행 단계**:
  ```csharp
  var mockObserver = Substitute.For<ICommObserver>();
  var factory = new TestMemoryConnectionFactory();
  var codec = new AsciiLineCodec(delimiter: 0x0A);
  var session = new KableSession<string>(factory, codec, mockObserver);

  await session.StartAsync();
  await Task.Delay(50); // 수신 루프 활성화 대기

  // 정상적인 우아한 종료 수행
  await session.StopAsync();

  // 검증: 정상 종료 과정에서 LogLevel.Error 또는 READ_LOOP_FAULT가 일체 호출되지 않아야 함
  mockObserver.DidNotReceive().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
      r.Level == LogLevel.Error ||
      r.Tag == "READ_LOOP_FAULT" ||
      r.Tag == "DISPATCH_LOOP_FAULT"));
  ```
- **기대 결과**:
  - 오직 하드웨어 비정상 단선(`IOException`, 소켓 리셋 등) 시에만 `LogLevel.Error`가 발행되며, 정상 정지 시에는 에러 로그가 발생하지 않음.

---

### 📌 TC_OBS_203: KableSession_TxFlushFailure_EmitsErrorRecordAndPropagatesFailFast
- **목적**:
  하드웨어 연결이 물리적으로 단선되거나 소켓이 강제 종료된 상태에서 상위 애플리케이션이 `RequestAsync`를 호출하여 패킷 출력 버퍼 플러시(`FlushAsync`) 시도 중 `IOException` 또는 `SocketException`이 발생했을 때, **`IO_FLUSH_ERROR` 태그와 함께 `LogLevel.Error` 레코드 즉시 발행 + 상위 호출자에게 `DeviceDisconnectedException` 페일패스트 즉시 전파 + 세션 폐쇄(`OnConnectionClosed`) 연계가 정확히 이루어지는지 검증**합니다.
- **사전 조건**:
  - 송신 파이프 출력 스트림이 이미 오류(`IOException`) 상태로 완료된 연결 컨텍스트 주입.
- **실행 단계**:
  ```csharp
  var mockObserver = Substitute.For<ICommObserver>();
  var factory = new TestMemoryConnectionFactory();
  var codec = new AsciiLineCodec(delimiter: 0x0A);
  await using var session = new KableSession<string>(factory, codec, mockObserver);
  await session.StartAsync();

  // 강제로 RemoteRead(송신 파이프의 반대편)를 예외 완료 처리하여 FlushAsync 실패 유도
  factory.Context.RemoteRead.Complete(new IOException("Hardware cable detached during flush"));

  Func<Task> act = async () => await session.RequestAsync<string>("TEST_REQ", TimeSpan.FromSeconds(2));
  
  // 페일패스트 예외 검증
  await act.Should().ThrowAsync<DeviceDisconnectedException>();

  // 관측성 에러 로그 발행 검증
  mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
      r.Level == LogLevel.Error &&
      r.Tag == "IO_FLUSH_ERROR" &&
      r.Kind == TrafficKind.SpontaneousAlarm));
  ```
- **기대 결과**:
  - I/O 송신 실패가 무음으로 유실되거나 블로킹되지 않고, 즉시 `LogLevel.Error` 관측 레코드로 기록되며 연결이 안전하게 종료됨.

---

## 4. 과도한 테스트 배제 항목 (Excluded Non-Essential Tests)

테스트 스위트의 실행 시간 단축과 유지보수 효율을 위해 다음 항목은 고의로 제외합니다:
1. **`PacketTraceRecord` 구조체 필드 할당 일치 검증**:
   - 생성자 매개변수와 프로퍼티의 단순 1:1 반환은 C# 컴파일러 수준에서 보장되므로 별도의 단위 테스트를 작성하지 않습니다.
2. **.NET BCL `Channel.CreateBounded` 내부 알고리즘 테스트**:
   - BCL 라이브러리 자체의 원자성 및 동시성 락 알고리즘은 닷넷 런타임의 테스트 영역이므로 중복 검증을 배제합니다.
