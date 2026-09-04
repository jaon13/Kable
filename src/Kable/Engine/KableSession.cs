namespace Kable.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Kable.Codecs;
using Kable.Core;
using Kable.Exceptions;
using Kable.Observability;

public sealed class KableSession<TMessage> : IDeviceSession<TMessage>
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IProtocolCodec<TMessage> _codec;
    private readonly ICommObserver? _observer;

    private readonly SemaphoreSlim _fifoLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TMessage>> _pendingRequests = new();
    private readonly Channel<TMessage> _incomingStream = Channel.CreateUnbounded<TMessage>(new UnboundedChannelOptions { SingleWriter = true });

    private IConnectionContext? _context;
    private Task? _readLoopTask;
    private TaskCompletionSource<TMessage>? _currentFifoTcs;
    private readonly CancellationTokenSource _sessionCts = new();
    private int _isConnected;

    public bool IsConnected => Volatile.Read(ref _isConnected) == 1;

    public async IAsyncEnumerable<TMessage> GetStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token, ct);
        while (await _incomingStream.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
        {
            while (_incomingStream.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }

    public IAsyncEnumerable<TMessage> Stream => GetStreamAsync();

    public KableSession(
        IConnectionFactory connectionFactory,
        IProtocolCodec<TMessage> codec,
        ICommObserver? observer = null)
    {
        _connectionFactory = connectionFactory;
        _codec = codec;
        _observer = observer;
    }

    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isConnected, 1, 0) != 0) return;

        _context = await _connectionFactory.ConnectAsync(ct).ConfigureAwait(false);
        _context.ConnectionClosed.Register(OnConnectionClosed);
        _readLoopTask = Task.Run(ReadLoopAsync);
    }

    public async ValueTask SendAsync(TMessage message, CancellationToken ct = default)
    {
        EnsureConnected();
        _codec.Encode(message, _context!.Output);
        await _context.Output.FlushAsync(ct).ConfigureAwait(false);

        _observer?.OnPacketTrace(new PacketTraceRecord(
            DateTime.UtcNow, PacketDirection.Tx, TrafficKind.AperiodicCommand,
            "SEND", ReadOnlyMemory<byte>.Empty, message?.ToString(), TimeSpan.Zero));
    }

    public async ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();

        if (!_codec.SupportsCorrelationId)
        {
            await _fifoLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var tcs = new TaskCompletionSource<TMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                _currentFifoTcs = tcs;

                _codec.Encode(request, _context!.Output);
                await _context.Output.FlushAsync(ct).ConfigureAwait(false);

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var responseTask = tcs.Task;
                var completedTask = await Task.WhenAny(responseTask, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);

                if (completedTask == responseTask)
                {
                    var response = await responseTask.ConfigureAwait(false);
                    sw.Stop();
                    _observer?.OnPacketTrace(new PacketTraceRecord(
                        DateTime.UtcNow, PacketDirection.Tx, TrafficKind.AperiodicCommand,
                        "REQUEST_RESP", ReadOnlyMemory<byte>.Empty, response?.ToString(), sw.Elapsed));

                    if (response is TResponse typedRes) return typedRes;
                    throw new InvalidCastException($"Expected {typeof(TResponse).Name}, received {response?.GetType().Name}");
                }

                if (timeoutCts.IsCancellationRequested)
                {
                    throw new DeviceTimeoutException(request?.ToString() ?? "UnknownCommand", timeout);
                }

                throw new OperationCanceledException(ct);
            }
            finally
            {
                _currentFifoTcs = null;
                _fifoLock.Release();
            }
        }
        else
        {
            var cid = _codec.ExtractCorrelationId(request) ?? Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<TMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[cid] = tcs;

            try
            {
                _codec.Encode(request, _context!.Output);
                await _context.Output.FlushAsync(ct).ConfigureAwait(false);

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var responseTask = tcs.Task;
                var completedTask = await Task.WhenAny(responseTask, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);

                if (completedTask == responseTask)
                {
                    var response = await responseTask.ConfigureAwait(false);
                    if (response is TResponse typedRes) return typedRes;
                    throw new InvalidCastException($"Expected {typeof(TResponse).Name}, received {response?.GetType().Name}");
                }

                throw new DeviceTimeoutException(request?.ToString() ?? "UnknownCommand", timeout);
            }
            finally
            {
                _pendingRequests.TryRemove(cid, out _);
            }
        }
    }

    public async ValueTask SendUrgentAsync(TMessage urgentMessage)
    {
        EnsureConnected();
        _codec.Encode(urgentMessage, _context!.Output);
        await _context.Output.FlushAsync().ConfigureAwait(false);

        _observer?.OnPacketTrace(new PacketTraceRecord(
            DateTime.UtcNow, PacketDirection.Tx, TrafficKind.AperiodicCommand,
            "URGENT_OOB", ReadOnlyMemory<byte>.Empty, urgentMessage?.ToString(), TimeSpan.Zero));
    }

    private async Task ReadLoopAsync()
    {
        var reader = _context!.Input;
        try
        {
            while (!_sessionCts.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(_sessionCts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                while (_codec.TryDecode(ref buffer, out var message))
                {
                    DispatchMessage(message);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted || result.IsCanceled) break;
            }
        }
        catch
        {
            // 단선 또는 예외 감지 시 루프 종료
        }
        finally
        {
            OnConnectionClosed();
        }
    }

    private void DispatchMessage(TMessage message)
    {
        if (_codec.IsAutonomousMessage(message))
        {
            _incomingStream.Writer.TryWrite(message);
            _observer?.OnPacketTrace(new PacketTraceRecord(
                DateTime.UtcNow, PacketDirection.Rx, TrafficKind.SpontaneousAlarm,
                "STREAM", ReadOnlyMemory<byte>.Empty, message?.ToString(), TimeSpan.Zero));
            return;
        }

        if (!_codec.SupportsCorrelationId)
        {
            if (_currentFifoTcs != null && !_currentFifoTcs.Task.IsCompleted)
            {
                _currentFifoTcs.TrySetResult(message);
                return;
            }
        }
        else
        {
            var cid = _codec.ExtractCorrelationId(message);
            if (cid != null && _pendingRequests.TryRemove(cid, out var tcs))
            {
                tcs.TrySetResult(message);
                return;
            }
        }

        // 응답 대기자가 없으면 일반 스트림 채널로 발행
        _incomingStream.Writer.TryWrite(message);
    }

    private void OnConnectionClosed()
    {
        if (Interlocked.Exchange(ref _isConnected, 0) == 1)
        {
            var ex = new DeviceDisconnectedException("하드웨어 연결이 단절되었습니다. (Fail-Fast 정책으로 모든 요청 즉각 취소)");
            _currentFifoTcs?.TrySetException(ex);
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(ex);
            }
            _pendingRequests.Clear();
            _incomingStream.Writer.TryComplete(ex);
        }
    }

    private void EnsureConnected()
    {
        if (Volatile.Read(ref _isConnected) == 0 || _context == null)
        {
            throw new DeviceDisconnectedException("연결이 열려 있지 않습니다. StartAsync()를 먼저 호출하세요.");
        }
    }

    public async ValueTask StopAsync()
    {
        _sessionCts.Cancel();
        OnConnectionClosed();
        if (_context != null)
        {
            await _context.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _fifoLock.Dispose();
        _sessionCts.Dispose();
    }
}
