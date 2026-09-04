# 03. Observability & Logging Specification (관측성 및 로깅 규격)

> 본 문서는 주기적 폴링 데이터와 비정기 제어 명령을 분리하여 UI 및 로그에 라우팅하는 `ICommObserver` 복합 관측성 패턴과 추천 라이브러리 조합을 정의합니다.

---

## 1. 배경 및 문제의식: "철저한 관심사의 분리 (Separation of Concerns)"

- **통신 모듈(`Kable`)의 본분**:
  - 통신 엔진은 물리 I/O와 패킷 송수신에만 집중합니다.
  - 디스크 파일 쓰기, 로그 압축/삭제, DB 적재 등 무거운 디스크 작업은 엔진 내부에서 완전히 배제합니다. (0-GC 이벤트 발행만 담당)
- **주입된 로거(`Serilog` / DB)의 본분**:
  - 원본 보존 및 규제 준수(21 CFR Part 11)를 위한 무손실 파일 기록은 외부 주입된 로거 파이프라인에서 비동기로 수행합니다.
- **UI 모니터링의 본분**:
  - 실시간 UI 화면은 주기적 계측(센서)과 제어 콘솔(명령)이 서로 방해하지 않도록 **독립된 링버퍼(`DropOldest`)**로 분리하여 렉(Lag)과 메모리 누수를 원천 방지합니다.

---

## 2. 복합 관측성 규격 (`ICommObserver`)

```csharp
namespace Kable.Observability;

public enum TrafficKind
{
    /// <summary>
    /// 비정기 제어 명령 및 트랜잭션 응답 (사용자 조작, 시퀀스 실행, 긴급 E-STOP)
    /// </summary>
    AperiodicCommand,

    /// <summary>
    /// 주기적 상태 폴링 및 텔레메트리 (Heartbeat, 센서 주기 계측, Ping-Pong)
    /// </summary>
    PeriodicTelemetry,

    /// <summary>
    /// 장비가 스스로 발행하는 비동기 자발적 알람
    /// </summary>
    SpontaneousAlarm
}

public readonly record struct PacketTraceRecord(
    DateTime TimestampUtc,
    PacketDirection Direction,     // TX(송신) or RX(수신)
    TrafficKind Kind,              // 트래픽 분류 (주기적 vs 비정기적 vs 알람)
    string Tag,                    // 세부 식별 태그 (예: "TEMP_POLL", "VALVE_CMD")
    ReadOnlyMemory<byte> RawBytes, // 0-Allocation 원본 버퍼 슬라이스
    string? ParsedText,
    TimeSpan Latency               // 응답 RTT 지연시간
);

public interface ICommObserver
{
    // [엔진 -> 관측자 0-GC 통지]
    // 통신 엔진은 블로킹 없이 이 메서드를 호출하고 즉시 I/O 루프로 복귀합니다.
    void OnPacketTrace(PacketTraceRecord trace);

    // [UI 연동 전용 분리 채널 (DropOldest 링버퍼 적용)]
    // A. 주기적 계측 전용 스트림 -> UI 상단 "실시간 대시보드(게이지/차트)" 직결
    ChannelReader<PacketTraceRecord> PeriodicStream { get; }

    // B. 비정기 명령/응답 전용 스트림 -> UI 하단 "명령 제어 콘솔" 직결 (스크롤 안 밀림!)
    ChannelReader<PacketTraceRecord> CommandStream { get; }

    // C. 에러/알람 전용 스트림 -> UI "경보 알림 팝업" 직결
    ChannelReader<PacketTraceRecord> AlarmStream { get; }
}
```

---

## 3. 로깅 및 저장소 연동 가이드

통신 엔진은 `ICommObserver`를 통해 외부 로깅 생태계와 매끄럽게 결합됩니다:

1. **디스크 파일 영구 저장 (`Serilog` 연동)**:
   - `OnPacketTrace`에서 `Serilog`의 서브 로거로 라우팅.
   - `AperiodicCommand`와 `SpontaneousAlarm`은 매일 롤링되는 텍스트 로그(`commands-.log`)에 100% 무손실 기록.
   - `PeriodicTelemetry`는 불필요한 디스크 I/O를 막기 위해 수치 메트릭(`Meter/Gauge`)으로 메모리 집계하거나 별도 통계 파일로 축약.
2. **UI 터미널 렉 방지 (`ChannelOptions.DropOldest`)**:
   - UI용 `PeriodicStream`, `CommandStream`은 최대 1,000건 크기의 Bounded Channel을 사용하여 오래된 패킷을 자동으로 밀어냄으로써 100ms 고속 수신 시에도 데스크톱 UI 프리징 원천 방지.
