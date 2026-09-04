# 🏛️ Kable - System Design & Interface Specification

> **문서 상태**: 단일 진실 공급원 (Single Source of Truth)  
> **최종 갱신**: 2026-09-04  
> **관련 문서**: [PROJECT_SPEC.md](file:///d:/Johnny/Kable/docs/PROJECT_SPEC.md), [CONVENTIONS.md](file:///d:/Johnny/Kable/docs/CONVENTIONS.md), [02_CORE_INTERFACES.md](file:///d:/Johnny/Kable/docs/02_CORE_INTERFACES.md)

---

## 1. 전송 추상화 인터페이스 (`Kable.Core` & `Kable.Transports`)

### 1.1 `IConnectionContext`
Bedrock 및 ASP.NET Core Kestrel 파이프라인 표준을 준수하는 양방향 0-GC 스트림 컨텍스트입니다.

```csharp
namespace Kable.Core;

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

public interface IConnectionContext : IAsyncDisposable
{
    string ConnectionId { get; }
    string EndpointDescription { get; }
    PipeReader Input { get; }
    PipeWriter Output { get; }
    CancellationToken ConnectionClosed { get; }
    void Abort(string reason);
}
```

- **`TcpConnectionContext`**: `Socket.NoDelay = true` 고정, 비동기 `NetworkStream` 파이프 바인딩.
- **`NamedPipeConnectionContext`**: Windows/Linux 로컬 고속 IPC 파이프 바인딩.
- **`SerialPortConnectionContext`**: 산업용 시리얼(RS-232C) `SerialPort.BaseStream` 파이프 바인딩.

---

## 2. 직렬화 및 프레이밍 코덱 인터페이스 (`Kable.Codecs`)

### 2.1 `IProtocolCodec<TMessage>`
하부 전송 계층의 바이트 시퀀스와 상부 세션 계층의 메시지 객체를 0-Allocation으로 변환합니다.

```csharp
namespace Kable.Codecs;

using System.Buffers;

public interface IProtocolCodec<TMessage>
{
    bool SupportsCorrelationId { get; }
    bool TryDecode(ref ReadOnlySequence<byte> buffer, out TMessage message);
    void Encode(TMessage message, IBufferWriter<byte> output);
    string? ExtractCorrelationId(TMessage message);
    bool IsAutonomousMessage(TMessage message);
}
```

- **`AsciiLineCodec`**: 개행(`\n`, `\r\n`) 기준 프레이밍, `MaxFrameSize`(기본 64KB) OOM 방어 가드 내장.
- **자율 메시지 판별(`IsAutonomousMessage`)**: `$`, `#` 접두사를 자율 알람/텔레메트리로 즉각 분류.

---

## 3. 상호작용 및 세션 엔진 (`Kable.Engine`)

### 3.1 `IDeviceSession<TMessage>`
RSocket 스타일의 4대 상호작용 인터페이스 규격입니다.

```csharp
namespace Kable.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IDeviceSession<TMessage> : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }
    IAsyncEnumerable<TMessage> Stream { get; }
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync();
    ValueTask SendAsync(TMessage message, CancellationToken ct = default);
    ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default);
    ValueTask SendUrgentAsync(TMessage urgentMessage);
}
```

### 3.2 하이브리드 트랜잭션 라우팅 로직
- `_codec.SupportsCorrelationId == false`:
  - `_fifoLock.WaitAsync()` 선점형 비동기 락 획득 $\rightarrow$ 송신 $\rightarrow$ 응답 대기 $\rightarrow$ 완료 후 락 해제.
- `_codec.SupportsCorrelationId == true`:
  - 락 획득 없이 `ConcurrentDictionary<string, TaskCompletionSource>` 등록 $\rightarrow$ 즉시 송신 $\rightarrow$ 인터리빙 응답 비동기 매칭.
- **타임아웃 지연 응답 격리**: 타임아웃 만료 후 뒤늦게 도착한 패킷은 `_incomingStream` 채널로 자동 우회되어 다음 요청을 절대 오염시키지 않음.

---

## 4. 관측성 및 텔레메트리 (`Kable.Observability`)

### 4.1 3-Channel 링버퍼 구조
- **`PeriodicTelemetry`**: 초당 수백 건의 주기적 상태 패킷 (`DropOldest` 적용, UI 렉 원천 차단).
- **`AperiodicCommand`**: 제어 명령 및 응답 송수신 이력.
- **`SpontaneousAlarm`**: 장비 자율 경보 및 인터록 알람.
