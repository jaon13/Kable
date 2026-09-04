# 02. Core Interfaces Specification (핵심 인터페이스 규격)

> 본 문서는 BCL `System.IO.Pipelines` 기반의 전송 계층 인터페이스와 RSocket 스타일의 상위 세션 인터페이스를 정의합니다.

---

## 1. Bedrock 하부 연결 컨텍스트 (`IConnectionContext`)

모든 물리(TCP, Serial) 및 논리(NamedPipe IPC) 연결을 단일화하는 핵심 규격입니다:

```csharp
namespace Kable.Core;

using System.IO.Pipelines;

public interface IConnectionContext : IAsyncDisposable
{
    string ConnectionId { get; }
    string EndpointDescription { get; }
    
    // Bedrock 표준 0-GC 파이프
    PipeReader Input { get; }
    PipeWriter Output { get; }
    
    // 단절 알림 토큰 (OS KeepAlive 단절 감지)
    CancellationToken ConnectionClosed { get; }
    
    void Abort(string reason);
}

public interface IConnectionFactory
{
    ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default);
}

public interface IConnectionListener : IAsyncDisposable
{
    ValueTask<IConnectionContext> AcceptAsync(CancellationToken ct = default);
    void Stop();
}
```

---

## 2. 프로토콜 코덱 인터페이스 (`IProtocolCodec<TMessage>`)

와이어 바이트 스트림과 비즈니스 메시지 간의 양방향 0-할당 변환기입니다:

```csharp
namespace Kable.Codecs;

using System.Buffers;

public interface IProtocolCodec<TMessage>
{
    // 프로토콜이 시퀀스 번호/Correlation ID를 자체 지원하는지 여부
    // false일 경우 세션 엔진이 자동으로 선점형 FIFO 락(SemaphoreSlim)을 걸어 동시 호출 시 꼬임을 방지함
    bool SupportsCorrelationId { get; }

    // 수신 버퍼에서 메시지 프레임 디코딩 (Zero-Allocation)
    bool TryDecode(ref ReadOnlySequence<byte> buffer, out TMessage message);
    
    // 송신 메시지를 파이프 버퍼에 직렬화 인코딩
    void Encode(TMessage message, IBufferWriter<byte> output);
    
    // 요청-응답 매핑용 Correlation ID 추출 (SupportsCorrelationId == true일 때 사용)
    string? ExtractCorrelationId(TMessage message);

    // 요청 응답이 아닌 장비 자발적 텔레메트리/하트비트/알람인지 판별 (true면 RequestAsync 대기자를 깨우지 않고 Stream으로 직행)
    bool IsAutonomousMessage(TMessage message) => false;
}
```

---

## 3. 상부 RSocket 세션 인터페이스 (`IDeviceSession<TMessage>`)

장비 제어기가 호출하는 최상위 단일 인터페이스입니다:

```csharp
namespace Kable.Engine;

public interface IDeviceSession<TMessage> : IAsyncDisposable
{
    bool IsConnected { get; }
    
    // 1. 실시간 계측 데이터 스트림 구독 (Request-Stream / Channel)
    IAsyncEnumerable<TMessage> Stream { get; }
    
    // 2. 단방향 명령 전송 (Fire-and-Forget)
    ValueTask SendAsync(TMessage message, CancellationToken ct = default);
    
    // 3. 요청-응답 RPC (하이브리드 FIFO 락 or 인터리빙 + 워치독 격리)
    // 단선 시 DeviceDisconnectedException, 타임아웃 시 DeviceTimeoutException 발생 (Fail-Fast)
    ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default);
    
    // 4. 긴급 비상 정지 (Urgent OOB Injection)
    ValueTask SendUrgentAsync(TMessage urgentMessage);
    
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync();
}
```

---

## 4. 산업용 표준 예외 모델 (Fail-Fast Exception Hierarchy)

```csharp
namespace Kable.Exceptions;

/// <summary>
/// 물리/논리 통신선 단선 시 대기 중인 모든 요청에 즉각 발행되는 예외 (Fail-Fast)
/// </summary>
public class DeviceDisconnectedException : Exception
{
    public DeviceDisconnectedException(string message) : base(message) { }
}

/// <summary>
/// 장비 펌웨어 묵묵부답 또는 응답 지연 시 워치독에 의해 격리 발행되는 예외
/// </summary>
public class DeviceTimeoutException : TimeoutException
{
    public DeviceTimeoutException(string command, TimeSpan timeout)
        : base($"장비 명령 '{command}'이(가) 타임아웃({timeout.TotalSeconds:F1}s) 내에 응답하지 않았습니다.") { }
}
```

