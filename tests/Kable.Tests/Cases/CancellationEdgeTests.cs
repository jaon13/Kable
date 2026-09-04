namespace Kable.Tests.Cases;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
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
}
