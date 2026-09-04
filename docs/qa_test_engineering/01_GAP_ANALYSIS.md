# 01. Test Coverage Gap Analysis & Threat Modeling

> **Status**: Comprehensive Analysis & Evaluation  
> **Evaluator**: World-Class Test & QA Architect  
> **Target Scope**: `Kable` Core Solution (`src/Kable`, `src/Kable.Generators`, `src/Kable.Transport.Ipc`, `src/Kable.Engine.Disruptor`)

---

## 1. 개요 및 전체 테스트 스위트 통계

현재 Kable 솔루션은 xUnit 및 FluentAssertions 기반으로 구축된 강력한 테스트 스위트를 갖추고 있으며, 모든 테스트가 최신 빌드에서 100% 정상 통과하고 있습니다.

### 현재 테스트 수량 통계
- **`Kable.Tests`**: 총 105개 테스트
  - `Transports`: 14개 (TCP, NamedPipe, SerialPort mock, Listener, Fault Injection)
  - `Codecs`: 16개 (AsciiLineCodec, 1-byte sliding, Multi-segment, BinaryLengthPrefixed)
  - `Engine`: 72개 (KableSession, FIFO serialization, Out-of-Order multiplexing, Cancellation, Lifecycle, SpscRingBuffer, Watchdog)
  - `Observability`: 3개 (CommObserver Bounded ringbuffer, DropOldest, Multi-threaded burst)
- **`Kable.Generators.Tests`**: 총 5개 테스트
  - `SourceGeneratorExecutionTests`: Attribute 매핑, 단일/다중 파라미터 보간, IsUrgent 마커, Roslyn Driver In-Memory 컴파일
- **전체 통과율**: 110 / 110 (100% Pass)

---

## 2. 갭 분석 (Gap Analysis): 5대 핵심 잠재 취약 구역

테스트 엔지니어 관점에서 기능별 코드를 전수 분석한 결과, 일반적인 성공 경로(Happy Path)와 기본적인 오류 경로는 잘 방어되어 있으나, **산업용 하드웨어 통신 특유의 가혹한 엣지 조건**에서 누락된 테스트 케이스들이 식별되었습니다.

### ⚠️ Gap 1. Transport Layer (물리 I/O 계층)
1. **SerialPort 물리 I/O 실패 모의**:
   - `SerialPortConnectionContext`는 `System.IO.Ports.SerialPort`를 직접 래핑합니다. 현재 테스트는 `MemoryStream`을 주입한 mock 생성자로 `Abort`나 `DisposeAsync` 취소 토큰 여부만 테스트하고 있습니다.
   - **미흡점**: `SerialPort.Open()` 시 `SerialPort.GetPortNames()`에 존재하지 않는 동적 미할당 포트 지정, 다른 프로세스 점유로 인한 `UnauthorizedAccessException`, 전송 중 통신 패리티(Parity)/프레이밍 에러 발생 시 처리 검증 부족. (주의: `COM99`와 같은 특정 포트 번호 하드코딩은 배제하고, 런타임에 호스트 포트를 스캔하여 미점유 포트를 동적으로 산출해야 함)
2. **TcpConnectionContext 소켓 버퍼 만료 및 비정상 파이프라인 중단**:
   - 수신측이 전혀 읽지 않는 상태에서 대용량 데이터를 지속 `FlushAsync`할 때 소켓 버퍼가 가득 차는 Backpressure 상황에서의 안전한 취소 토큰 처리 검증.

### ⚠️ Gap 2. Codec Layer (프레이밍 및 직렬화 계층)
1. **특수 제어문자 및 널 바이트 경계**:
   - 바이너리 데이터 또는 ASCII 내 `\0`(Null Byte) 및 0x00~0x1F 사이의 제어문자가 포함된 비정상 노이즈 스트림 유입 시 파싱 안정성.
2. **다중 연속 빈 라인(Keep-Alive/Heartbeat)과 유효 메시지 혼합**:
   - 하드웨어 장비가 Keep-Alive 용도로 공백이나 `\r\n`만 지속적으로 방출할 때의 디스패치 채널 영향도 검증.

### ⚠️ Gap 3. Engine Layer (세션 및 상호작용 계층)
1. **`RequestAsync` 전송 중(FlushAsync 이전/도중) CancellationToken 취소**:
   - 전송을 시작하기 직전 취소, 전송 도중 취소 시 FIFO 락(`SemaphoreSlim`)이 누수 없이 온전히 해제되는지 확인.
2. **디스패치 루프(`_dispatchQueue`) 고갈 방어**:
   - 세션 내부 디스패치 큐는 용량 10,000의 Bounded Channel(`BoundedChannelFullMode.Wait`)로 설정되어 있습니다. 소비자(소프트웨어)가 과도하게 지연될 때 큐가 꽉 차서 I/O 루프까지 백프레셔가 안전하게 전파되는지 확인.
3. **`DisposeAsync` 후 `SendAsync`/`RequestAsync` 호출 시 예외 일관성**:
   - 리소스 정리 완료 후 인입되는 명령들이 `ObjectDisposedException` 또는 `DeviceDisconnectedException`으로 즉시 거부되는지 확인.

### ⚠️ Gap 4. Host & IPC Daemon (프로세스 분리 아키텍처)
1. **`Kable.Host` 데몬 프로세스 신뢰성**:
   - 다중 클라이언트의 동시 접속 및 일방적인 클라이언트 프로세스 비정상 종료(`Kill`) 시 호스트 데몬이 다운되지 않고 해당 세션을 클린업하는지 검증.

### ⚠️ Gap 5. Source Generator Layer (컴파일 타임 생성기)
1. **중첩 중괄호 `{}` 및 템플릿 포맷 오류**:
   - 템플릿 문자열에 정규식이나 JSON 형태(`oCMD:{"id":{Id}}`)가 포함될 때의 이스케이프 정상 동작 검증.

---

## 3. 과도하거나 불필요한 테스트 지양 원칙 (Anti-Pattern Rules)

테스트 스위트의 비대화와 유지보수 비용을 방지하기 위해 다음 항목은 명시적으로 테스트 대상에서 제외합니다:
1. **단순 프라이빗 멤버 접근 테스트**: 리플렉션을 통해 내부 변수 값을 강제로 검증하는 행위 금지.
2. **외부 라이브러리 자체 기능 테스트**: `System.IO.Pipelines`나 `System.Threading.Channels` 프레임워크 자체의 기본 동작 검증 지양.
3. **단순 Getter/Setter/Record 구조체 테스트**: 필드 할당 및 동등성 비교는 컴파일러가 보장하므로 불필요한 테스트 생성 배제.
