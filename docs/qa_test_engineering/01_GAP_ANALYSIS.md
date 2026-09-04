# 01. Kable 현행 테스트 분석 및 사각지대 (Gap Analysis)

> **문서 상태**: Approved Baseline  
> **분석 범위**: `src/Kable`, `src/Kable.Generators`, `tests/Kable.Tests`

---

## 1. 현행 테스트 현황 요약 (Current Test Baseline)

현재 `Kable.Tests` 스위트는 총 **75개의 단위 및 통합 테스트**로 구성되어 있으며, 100% 정상 통과하고 있습니다.

### 기존 테스트의 우수한 영역 (Strengths)
1. **ASCII 프레이밍 기초**: 단일/다중 세그먼트 파이프라인에서 개행 문자(`\n`, `\r\n`)를 이용한 프레이밍 정상 분할.
2. **단선 Fail-Fast**: `TestMemoryConnectionContext.Abort()` 시 대기 중인 모든 `RequestAsync`가 즉시 `DeviceDisconnectedException`을 수신.
3. **텔레메트리 관측성**: `CommObserver` 링버퍼 오버플로우 시 `DropOldest` 원칙에 따른 최신 텔레메트리 보존.
4. **기본적 소스 생성기**: `[DeviceCommand]` 어트리뷰트가 적용된 기본 레코드 구조체의 문자열 보간 포맷 검증.

---

## 2. 심층 사각지대 (Dead Zones & Gaps) 정밀 분석

테스트 전문가의 관점에서 철저하게 코드를 분해 분석한 결과, **프로덕션 환경(고부하, 물리 통신 장애, 비정상 패킷 주입)에서 잠재적 장애를 유발할 수 있는 6가지 중대한 결함 사각지대**가 식별되었습니다.

### 🔴 GAP 1: 코덱 버퍼 오버플로우 및 Delimiter 고갈 방어 부재 (OOM 취약성)
- **문제점**:
  - `AsciiLineCodec.TryDecode()`는 구분자(`_delimiter`)가 발견될 때까지 버퍼를 소비하지 않고 무한정 확장합니다(`reader.AdvanceTo(buffer.Start, buffer.End)`).
  - 계측 장비의 노이즈, 통신 선로 오염, 또는 펌웨어 오류로 인해 수 메가바이트(MB) 동안 구분자가 없는 패킷이 유입되면 메모리가 고갈되어 프로세스가 충돌할 수 있습니다.
- **누락된 테스트**:
  - 정의된 최대 프레임 크기(예: 64KB)를 초과하는 비정상 바이트 스트림 유입 시 안전 예외 발생 여부.
  - 불완전한 프레임이 장시간 대기할 때 파이프라인 내부 버퍼 크기 제어 검증.

### 🔴 GAP 2: 세션 동시성 엣지 케이스 (Lock Contention & Race Condition)
- **문제점**:
  - `KableSession`의 Correlation ID 모드(`_pendingRequests`)에서, 응답 매칭 직전에 클라이언트가 `CancellationToken`에 의해 취소되거나 타임아웃이 발생하는 타이밍의 경쟁 상태(Race Condition) 미검증.
  - `RequestAsync` 호출자가 폭증할 때 `_fifoLock.WaitAsync()`의 공정성(Fairness) 및 세션 정지(`StopAsync`) 시 대기 락 즉시 해제 보증 검증 미흡.
- **누락된 테스트**:
  - 다중 스레드(50+ Threads)에서 무작위 타임아웃과 취소를 동반한 동시성 스트레스 테스트.
  - 타임아웃 후 응답이 10ms 뒤에 도착했을 때, 이것이 일반 스트림(`Stream`) 채널로 정상 격리되는지(`DispatchMessage`) 검증.

### 🔴 GAP 3: 시리얼 통신(RS-232C) 하드웨어 예외 및 수명주기 테스트 부재
- **문제점**:
  - `SerialPortConnectionContext`는 실제 `System.IO.Ports.SerialPort`의 특성(USB-to-Serial 변환기 뽑힘, 드라이버 멈춤, 패리티 에러, 하드웨어 플로우 제어 RTS/CTS 지연)을 완전히 시뮬레이션하지 못하고 있습니다.
  - 특히 포트가 이미 다른 프로세스에 의해 점유되어 있거나, 통신 중 USB 케이블이 물리적으로 분리될 때의 `IOException` 처리 테스트 부재.
- **누락된 테스트**:
  - 통신 도중 강제 Close/Dispose 시 PipeReader/PipeWriter의 안전한 Complete 처리.
  - RTS/DTR 신호 토글 및 하드웨어 흐름 제어 활성화 시 I/O 블로킹 방어 테스트.

### 🟡 GAP 4: TCP 네트워크 Half-Open 및 소켓 백로그 버퍼 포화
- **문제점**:
  - 소켓이 물리적으로 끊어졌으나 FIN/RST 패킷이 전달되지 않는 Silent Drop(Half-Open) 상황에서, 송신 파이프라인(`FlushAsync`)이 소켓 버퍼를 채우다가 무한 블로킹되는 현상 방어 검증 부족.
- **누락된 테스트**:
  - 원격지에서 수신을 중단하고 소켓 윈도우 크기를 0으로 줄였을 때(Zero Window Probe), Kable 송신부 타임아웃 및 Fail-Fast 반응.

### 🟡 GAP 5: 세션 수명주기(Lifecycle) 중첩 호출 및 비정상 순서
- **문제점**:
  - `StartAsync`를 두 번 연속 호출하거나, 이미 `StopAsync`된 세션에 `SendAsync`를 호출하는 경우.
  - `DisposeAsync`가 실행되는 도중 진행 중이던 `RequestAsync`가 깨끗하게 중단되는지 여부.
- **누락된 테스트**:
  - 멱등성(Idempotency) 검증: `StartAsync` n회 호출 시 단일 소켓만 연결 유지.
  - `DisposeAsync` 도중의 동시 요청 처리 검증.

### 🟡 GAP 6: Source Generator의 에러 진단(Diagnostics) 및 복합 프로토콜
- **문제점**:
  - 현재 `SourceGeneratorTests`는 정상적인 파라미터 보간 케이스만 테스트함.
  - 템플릿 내 프로퍼티 매칭 실패, 문법적 오류가 있는 어트리뷰트 선언 시 컴파일 경고/에러가 정상 발행되는지 미검증.

---

## 3. 리스크 평가 및 보완 우선순위 매트릭스

| 우선순위 | 영역 | 결함 식별자 | 핵심 위험 요인 | 조치 방안 |
| :--- | :--- | :--- | :--- | :--- |
| **P0 (최우선)** | Codecs | GAP-01 | 악의적 패킷/노이즈에 의한 메모리 고갈 (OOM) | `MaxFrameSize` 초과 시 차단 테스트 |
| **P0 (최우선)** | Engine | GAP-02 | 타임아웃과 응답 도착 타이밍 경합 시 누수 | 타임아웃 엣지 레이스 컨디션 테스트 |
| **P1 (중요)** | Transport | GAP-03 | RS-232C USB 탈락 시 장비 락업(Lock-up) | 시리얼 통신 가상 결함 주입 테스트 |
| **P1 (중요)** | Transport | GAP-04 | 네트워크 Half-Open 시 소켓 영구 블로킹 | TCP 제로 윈도우 및 송신 타임아웃 테스트 |
| **P2 (보통)** | Engine | GAP-05 | 수명주기 API 멱등성 파괴 및 상태 꼬임 | 다중 Start/Stop 동시 호출 테스트 |
| **P2 (보통)** | Generators | GAP-06 | 잘못된 프로토콜 정의 시 개발자 혼란 | 소스 생성기 Diagnostics 컴파일러 테스트 |
