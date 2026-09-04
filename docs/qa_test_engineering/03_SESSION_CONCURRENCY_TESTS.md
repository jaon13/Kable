# 03. Session Concurrency & Resilience Test Specification

> **Document Status**: Approved Technical Specification  
> **Target Modules**: `Kable.Engine`, `KableSession<T>`, `IDeviceSession<T>`  

---

## 1. 개요 및 검증 목표

`KableSession`은 비동기 RSocket 상호작용 패턴(`RequestAsync`, `SendAsync`, `Stream`, `SendUrgentAsync`)을 제공하는 핵심 오케스트레이션 엔진입니다.
하드웨어 통신의 특성상 고빈도 동시 요청과 타임아웃, 예외 발생 시의 락 복원력(Lock Recovery)과 격리(Isolation)가 완벽해야 합니다.

---

## 2. 심층 테스트 케이스 명세

### TC-SES-101: 100개 스레드 동시 FIFO 요청 공정성 (기존 검증 완료)
- **우선순위**: P0
- **목표**: 100개의 동시 작업이 비-상관(Non-Correlation) 단일 채널 환경에서 데드락 없이 1:1 순차 매칭 수행.

### TC-SES-102: 타임아웃 이후 지연 도착한 Phantom 응답 격리 (기존 검증 완료)
- **우선순위**: P0
- **목표**: 타임아웃 만료 후 뒤늦게 도착한 패킷이 다음 정상 요청의 응답으로 혼입되지 않고 `Stream` 채널로 안전하게 전환.

### TC-SES-109: [NEW] RequestAsync 실행 중 외부 CancellationToken 취소 시 락 반환 검증
- **우선순위**: P0
- **목표**: 호출자가 부여한 `CancellationToken`이 응답 수신 전 취소(`cts.Cancel()`)되었을 때, `OperationCanceledException`이 정상 방출되고 내부 `_fifoLock`이 안전하게 해제되어 다음 대기 요청이 즉시 실행될 수 있는지 검증.
- **검증 단언**:
  ```csharp
  var cts = new CancellationTokenSource();
  var task1 = session.RequestAsync<string>("CMD1", TimeSpan.FromSeconds(5), cts.Token);
  cts.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task1.AsTask());
  
  // 이후 다음 요청이 즉시 정상 완료되어야 함 (락 고착 방지)
  var task2 = session.RequestAsync<string>("CMD2", TimeSpan.FromSeconds(2));
  await factory.Context.WriteAsciiLineAsync("RESP2", 0x0A);
  (await task2).Should().Be("RESP2");
  ```

### TC-SES-110: [NEW] 응답 타입 캐스팅 실패(InvalidCastException) 시 락 해제 보장
- **우선순위**: P1
- **목표**: `RequestAsync<int>`로 요청했으나 응답 객체 타입이 맞지 않아 `InvalidCastException`이 발생할 경우, 세션 엔진의 내부 세마포어가 고갈되지 않고 다음 호출자에게 제어권이 이양되는지 검증.

### TC-SES-111: [NEW] 스트림 다중 반복기(Multi-Consumer) 구독 격리
- **우선순위**: P2
- **목표**: 두 개의 독립된 백그라운드 태스크가 각각 `session.GetStreamAsync()`를 호출하여 메시지를 대기할 때 채널 읽기 경합 또는 예외 없이 자율 알람이 정상 소비되는지 검증.

### TC-SES-112: [NEW] 연결 단절 상태에서 RequestAsync 호출 즉시 Fail-Fast
- **우선순위**: P1
- **목표**: `StartAsync()`가 호출되지 않았거나 `StopAsync()`로 종료된 상태에서 송신/요청 시 대기 없이 즉시 `DeviceDisconnectedException`을 방출하는지 검증.
