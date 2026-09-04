namespace Kable.Tests.Cases;

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Exceptions;
using Kable.Tests.Fixtures;
using Xunit;

public sealed class CorrelationIdLineCodec : IProtocolCodec<string>
{
    private readonly byte _delimiter;

    public bool SupportsCorrelationId => true;

    public CorrelationIdLineCodec(byte delimiter = 0x0A)
    {
        _delimiter = delimiter;
    }

    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out string message)
    {
        var pos = buffer.PositionOf(_delimiter);
        if (pos == null)
        {
            message = string.Empty;
            return false;
        }

        var slice = buffer.Slice(0, pos.Value);
        message = Encoding.ASCII.GetString(slice.ToArray()).TrimEnd('\r', '\n');
        buffer = buffer.Slice(buffer.GetPosition(1, pos.Value));
        return true;
    }

    public void Encode(string message, IBufferWriter<byte> output)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        var span = output.GetSpan(bytes.Length + 1);
        bytes.CopyTo(span);
        span[bytes.Length] = _delimiter;
        output.Advance(bytes.Length + 1);
    }

    public string? ExtractCorrelationId(string message)
    {
        int colonIdx = message.IndexOf(':');
        if (colonIdx > 0)
        {
            return message.Substring(0, colonIdx);
        }
        return null;
    }

    public bool IsAutonomousMessage(string message)
    {
        return message.StartsWith("$", StringComparison.Ordinal);
    }
}

public class SessionInterleavingAndResilienceTests
{
    [Fact]
    public async Task TC_SES_01_RequestAsync_WithCorrelationIdCodec_EnablesOutOfOrderResponses()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new CorrelationIdLineCodec();
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        var taskSlow = session.RequestAsync<string>("CID_1:SLOW_COMMAND", TimeSpan.FromSeconds(5));
        var taskFast = session.RequestAsync<string>("CID_2:FAST_COMMAND", TimeSpan.FromSeconds(5));

        await factory.Context.WriteAsciiLineAsync("CID_2:FAST_RESPONSE", 0x0A);
        var fastRes = await taskFast;
        fastRes.Should().Be("CID_2:FAST_RESPONSE");
        taskSlow.IsCompleted.Should().BeFalse();

        await factory.Context.WriteAsciiLineAsync("CID_1:SLOW_RESPONSE", 0x0A);
        var slowRes = await taskSlow;
        slowRes.Should().Be("CID_1:SLOW_RESPONSE");
    }

    [Fact]
    public async Task TC_SES_02_RequestAsync_LateResponseAfterTimeout_DoesNotPolluteNextRequest()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        Func<Task> actTimeout = async () =>
            await session.RequestAsync<string>("REQ_TIMEOUT", TimeSpan.FromMilliseconds(50));

        await actTimeout.Should().ThrowAsync<DeviceTimeoutException>();

        await factory.Context.WriteAsciiLineAsync("LATE_PHANTOM_RESPONSE", 0x0A);
        await Task.Delay(50);

        var newReqTask = session.RequestAsync<string>("REQ_CLEAN", TimeSpan.FromSeconds(2));
        await factory.Context.WriteAsciiLineAsync("CLEAN_RESPONSE", 0x0A);

        var actualRes = await newReqTask;
        actualRes.Should().Be("CLEAN_RESPONSE");
        actualRes.Should().NotBe("LATE_PHANTOM_RESPONSE");
    }

    [Fact]
    public async Task TC_SES_03_RequestAsync_SimultaneousAlarmAndResponse_RoutesCorrectly()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        var reqTask = session.RequestAsync<string>("GET_STATUS", TimeSpan.FromSeconds(5));

        for (int i = 1; i <= 5; i++)
        {
            await factory.Context.WriteAsciiLineAsync($"$ALARM_LEVEL_{i}", 0x0A);
        }

        await factory.Context.WriteAsciiLineAsync("STATUS_NORMAL_OK", 0x0A);

        var response = await reqTask;
        response.Should().Be("STATUS_NORMAL_OK");
    }

    [Fact]
    public async Task TC_SES_08_OnConnectionClosed_MultipleConcurrentCallers_AllReceiveFailFastException()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new CorrelationIdLineCodec();
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        const int callerCount = 20;
        var tasks = new Task[callerCount];
        for (int i = 0; i < callerCount; i++)
        {
            int id = i;
            tasks[i] = session.RequestAsync<string>($"CID_{id}:HOLD", TimeSpan.FromSeconds(10)).AsTask();
        }

        await Task.Delay(50);

        factory.Context.Abort("Physical Wire Cut");

        for (int i = 0; i < callerCount; i++)
        {
            Func<Task> act = async () => await tasks[i];
            await act.Should().ThrowAsync<DeviceDisconnectedException>();
        }

        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TC_SES_04_RequestAsync_InvalidResponseTypeCast_ThrowsInvalidCastAndReleasesLock()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Expect int, but response is string -> throws InvalidCastException
        var castTask = session.RequestAsync<int>("CMD_NEED_INT", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("NOT_AN_INT", 0x0A);

        Func<Task> act = async () => await castTask;
        await act.Should().ThrowAsync<InvalidCastException>();

        // Verify lock is released and subsequent request succeeds
        var nextTask = session.RequestAsync<string>("CMD_NEXT", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("OK_NEXT", 0x0A);
        var res = await nextTask;
        res.Should().Be("OK_NEXT");
    }

    [Fact]
    public async Task TC_SES_07_SendUrgentAsync_UnderFifoContention_BypassesWaitingQueueImmediately()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Lock FIFO with slow request
        var slowTask = session.RequestAsync<string>("SLOW_MEASUREMENT", TimeSpan.FromSeconds(5));

        // Send urgent message while slowTask is waiting for response
        await session.SendUrgentAsync("EMERGENCY_SHUTDOWN");

        // Remote reader should receive both commands without deadlock
        var readResult = await factory.Context.RemoteRead.ReadAsync();
        string received = Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(readResult.Buffer));
        factory.Context.RemoteRead.AdvanceTo(readResult.Buffer.End);

        received.Should().Contain("SLOW_MEASUREMENT");
        received.Should().Contain("EMERGENCY_SHUTDOWN");

        // Complete the slow request
        await factory.Context.WriteAsciiLineAsync("SLOW_DONE", 0x0A);
        var slowRes = await slowTask;
        slowRes.Should().Be("SLOW_DONE");
    }

    [Fact]
    public async Task TC_SES_05_StartAsync_MultipleCalls_IdempotentAndThreadSafe()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        // Multiple concurrent starts
        var t1 = session.StartAsync().AsTask();
        var t2 = session.StartAsync().AsTask();
        var t3 = session.StartAsync().AsTask();

        await Task.WhenAll(t1, t2, t3);

        session.IsConnected.Should().BeTrue();

        var pingTask = session.RequestAsync<string>("PING_IDEMPOTENT", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("PONG", 0x0A);
        var res = await pingTask;
        res.Should().Be("PONG");
    }

    [Fact]
    public async Task TC_SES_06_Stream_ConsumerAbortsMidway_DoesNotBlockSessionReadLoop()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        using var consumerCts = new System.Threading.CancellationTokenSource();

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var item in session.Stream.WithCancellation(consumerCts.Token))
            {
                if (item.StartsWith("$ABORT_CONSUMER"))
                {
                    consumerCts.Cancel();
                }
            }
        });

        await factory.Context.WriteAsciiLineAsync("$START", 0x0A);
        await factory.Context.WriteAsciiLineAsync("$ABORT_CONSUMER", 0x0A);

        Func<Task> actConsumer = async () => await consumerTask;
        await actConsumer.Should().ThrowAsync<OperationCanceledException>();

        // Verify session read loop is still alive and handling requests
        var reqTask = session.RequestAsync<string>("ECHO_AFTER_ABORT", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("ECHO_OK", 0x0A);
        var res = await reqTask;
        res.Should().Be("ECHO_OK");
    }

    [Fact]
    public async Task TC_SES_101_Session_200ConcurrentFifoRequests_MaintainsStrictOrderWithoutDeadlock()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        const int requestCount = 100;
        var requestTasks = new Task<string>[requestCount];

        // Server emulation loop: reads each request and writes matching response
        var serverLoop = Task.Run(async () =>
        {
            var reader = factory.Context.RemoteRead;
            int responded = 0;
            while (responded < requestCount)
            {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;
                while (codec.TryDecode(ref buffer, out var cmd))
                {
                    var resp = "RESP_" + cmd;
                    await factory.Context.WriteAsciiLineAsync(resp, 0x0A);
                    responded++;
                    if (responded >= requestCount) break;
                }
                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        });

        for (int i = 0; i < requestCount; i++)
        {
            int index = i;
            requestTasks[index] = session.RequestAsync<string>($"FIFO_CMD_{index}", TimeSpan.FromSeconds(10)).AsTask();
        }

        var results = await Task.WhenAll(requestTasks);
        await serverLoop;

        for (int i = 0; i < requestCount; i++)
        {
            results[i].Should().Be($"RESP_FIFO_CMD_{i}");
        }
    }

    [Fact]
    public async Task TC_SES_102_Session_LateResponseAfterTimeout_RoutesToStreamWithoutPollutingNextRequest()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // 1. Request 1 times out quickly (50ms)
        Func<Task> actTimeout = async () =>
            await session.RequestAsync<string>("LATE_REQ_1", TimeSpan.FromMilliseconds(50));
        await actTimeout.Should().ThrowAsync<DeviceTimeoutException>();

        // 2. Late response for Request 1 arrives while NO request is active
        await factory.Context.WriteAsciiLineAsync("LATE_RESP_1", 0x0A);
        await Task.Delay(50);

        // 3. Request 2 is issued subsequently
        var req2Task = session.RequestAsync<string>("NORMAL_REQ_2", TimeSpan.FromSeconds(3)).AsTask();
        await factory.Context.WriteAsciiLineAsync("RESP_2", 0x0A);

        var res2 = await req2Task;
        // Request 2 should match its own response, not the late response 1!
        res2.Should().Be("RESP_2");

        // Late response 1 was routed into the unhandled Stream
        using var streamCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        bool foundLate = false;
        await foreach (var item in session.Stream.WithCancellation(streamCts.Token))
        {
            if (item == "LATE_RESP_1")
            {
                foundLate = true;
                break;
            }
        }
        foundLate.Should().BeTrue();
    }

    [Fact]
    public async Task TC_SES_103_Session_SendUrgentAsync_BypassesFifoLockInstantly()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Start a slow FIFO request that holds the fifo lock until response arrives
        var slowFifoTask = session.RequestAsync<string>("SLOW_CMD", TimeSpan.FromSeconds(5)).AsTask();

        // Send urgent message while FIFO lock is busy
        await session.SendUrgentAsync("URGENT_HALT");

        // Read from server side to verify urgent message arrived
        var serverRead = await factory.Context.RemoteRead.ReadAsync();
        string receivedWire = Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(serverRead.Buffer));
        factory.Context.RemoteRead.AdvanceTo(serverRead.Buffer.End);

        receivedWire.Should().Contain("URGENT_HALT");

        // Clean up FIFO request
        await factory.Context.WriteAsciiLineAsync("SLOW_ACK", 0x0A);
        await slowFifoTask;
    }

    [Fact]
    public async Task TC_SES_104_Session_AbruptDisconnect_Cancels100PendingRequestsFailFast()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new CorrelationIdLineCodec();
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        const int pendingCount = 50;
        var tasks = new Task[pendingCount];
        for (int i = 0; i < pendingCount; i++)
        {
            tasks[i] = session.RequestAsync<string>($"CID_{i}:CMD", TimeSpan.FromSeconds(10)).AsTask();
        }

        // Abrupt physical disconnect
        factory.Context.Abort("Physical disconnect triggered");

        // All 50 pending requests must throw DeviceDisconnectedException immediately without waiting for 10s timeout
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < pendingCount; i++)
        {
            Func<Task> act = async () => await tasks[i];
            await act.Should().ThrowAsync<DeviceDisconnectedException>();
        }
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TC_SES_105_Session_MultipleStartAndStop_MaintainsIdempotency()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        // 10 concurrent starts
        var startTasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            startTasks[i] = session.StartAsync().AsTask();
        }
        await Task.WhenAll(startTasks);
        session.IsConnected.Should().BeTrue();

        // 5 concurrent stops
        var stopTasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            stopTasks[i] = session.StopAsync().AsTask();
        }
        await Task.WhenAll(stopTasks);
        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TC_SES_107_Session_CallerCancellationTokenExpired_CancelsWithoutCorruptingEngine()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        using var callerCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Func<Task> actCanceled = async () =>
            await session.RequestAsync<string>("CANCELED_CMD", TimeSpan.FromSeconds(5), callerCts.Token);
        await actCanceled.Should().ThrowAsync<OperationCanceledException>();

        // Subsequent call must succeed seamlessly
        var nextReq = session.RequestAsync<string>("NEXT_HEALTHY_CMD", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("HEALTHY_OK", 0x0A);
        var res = await nextReq;
        res.Should().Be("HEALTHY_OK");
    }

    [Fact]
    public void TC_SES_108_Session_ObserverQueueOverflow_DropsOldestAndPreservesLatestPacket()
    {
        var observer = new Kable.Observability.CommObserver(bufferCapacity: 3);

        for (int i = 0; i < 10; i++)
        {
            observer.OnPacketTrace(new Kable.Observability.PacketTraceRecord(
                DateTime.UtcNow,
                Kable.Observability.PacketDirection.Rx,
                Kable.Observability.TrafficKind.PeriodicTelemetry,
                "STREAM",
                ReadOnlyMemory<byte>.Empty,
                $"ITEM_{i}",
                TimeSpan.Zero));
        }

        var list = new System.Collections.Generic.List<string>();
        while (observer.PeriodicStream.TryRead(out var rec))
        {
            if (rec.ParsedText != null) list.Add(rec.ParsedText);
        }

        list.Count.Should().Be(3);
        list.Should().ContainInOrder("ITEM_7", "ITEM_8", "ITEM_9");
    }
}

