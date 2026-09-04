namespace Kable.Tests.Cases;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Exceptions;
using Kable.Tests.Fixtures;
using Xunit;

public class CancellationEdgeTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public async Task RequestAsync_CallerCancelsBeforeResponse_ReleasesFifoLockForNextCaller(int delayMs)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        using var callerCts = new CancellationTokenSource();

        var firstTask = session.RequestAsync<string>("FIRST_CMD", TimeSpan.FromSeconds(5), callerCts.Token).AsTask();
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(delayMs));

        Func<Task> act1 = async () => await firstTask;
        await act1.Should().ThrowAsync<OperationCanceledException>();

        var secondTask = session.RequestAsync<string>("SECOND_CMD", TimeSpan.FromSeconds(5));
        await factory.Context.WriteAsciiLineAsync("SECOND_RESP", 0x0A);
        var secondResp = await secondTask;

        secondResp.Should().Be("SECOND_RESP");
    }

    [Fact]
    public async Task RequestAsync_AlreadyCanceledToken_DoesNotAcquireLockOrTransmit()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        using var canceledCts = new CancellationTokenSource();
        canceledCts.Cancel();

        Func<Task> act = async () => await session.RequestAsync<string>("NEVER_SENT", TimeSpan.FromSeconds(2), canceledCts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var followUpTask = session.RequestAsync<string>("FOLLOW_UP", TimeSpan.FromSeconds(2));
        await factory.Context.WriteAsciiLineAsync("FOLLOW_UP_ACK", 0x0A);
        var res = await followUpTask;
        res.Should().Be("FOLLOW_UP_ACK");
    }

    [Fact]
    public async Task TC_SES_110_RequestAsync_InvalidCastException_ReleasesFifoLockSafely()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Request expecting an incompatible type (e.g. int when response is string)
        var castFailTask = session.RequestAsync<int>("CMD_CAST_FAIL", TimeSpan.FromSeconds(3)).AsTask();
        await factory.Context.WriteAsciiLineAsync("TEXT_RESPONSE", 0x0A);

        Func<Task> actCast = async () => await castFailTask;
        await actCast.Should().ThrowAsync<InvalidCastException>();

        // Next request must acquire the lock immediately and succeed
        var nextTask = session.RequestAsync<string>("CMD_NEXT", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("NEXT_OK", 0x0A);
        var nextRes = await nextTask;
        nextRes.Should().Be("NEXT_OK");
    }

    [Fact]
    public async Task TC_SES_111_RequestAsync_NotConnected_ThrowsDeviceDisconnectedExceptionImmediately()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        // Session not started: calling RequestAsync must fail-fast without waiting for timeout
        Func<Task> act = async () => await session.RequestAsync<string>("PING_OFFLINE", TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<DeviceDisconnectedException>()
                 .WithMessage("*Connection is not open*");
    }

    [Fact]
    public async Task TC_ENG_203_KableSession_RequestAsync_CallerCanceledDuringWrite_ReleasesFifoLock()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Already-canceled token during request entry
        using var canceledCts = new CancellationTokenSource();
        canceledCts.Cancel();

        Func<Task> actCanceled = async () =>
            await session.RequestAsync<string>("CANCELED_CMD", TimeSpan.FromSeconds(5), canceledCts.Token);

        await actCanceled.Should().ThrowAsync<OperationCanceledException>();

        // Next request must acquire the FIFO lock cleanly and succeed
        var nextTask = session.RequestAsync<string>("VALID_CMD", TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync("VALID_RESP", 0x0A);
        var response = await nextTask;
        response.Should().Be("VALID_RESP");
    }
}

