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

public class WatchdogTimeoutAndDisconnectTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task RequestAsync_HardwareSilence_ThrowsDeviceTimeoutExceptionAndReleasesLock(int timeoutMs)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        Func<Task> actTimeout = async () => await session.RequestAsync<string>("SILENT_CMD", TimeSpan.FromMilliseconds(timeoutMs));

        var ex = await actTimeout.Should().ThrowAsync<DeviceTimeoutException>();
        ex.Which.Command.Should().Be("SILENT_CMD");


        var nextTask = session.RequestAsync<string>("NEXT_CMD", TimeSpan.FromSeconds(2));
        await factory.Context.WriteAsciiLineAsync("NEXT_ACK", 0x0A);
        var res = await nextTask;
        res.Should().Be("NEXT_ACK");
    }

    [Fact]
    public async Task RequestAsync_HardwareDisconnectedDuringWait_ThrowsDeviceDisconnectedExceptionFailFast()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        var pendingTask = session.RequestAsync<string>("WAITING_CMD", TimeSpan.FromSeconds(5)).AsTask();

        factory.Context.Abort("Physical Cable Disconnected");

        Func<Task> act = async () => await pendingTask;
        await act.Should().ThrowAsync<DeviceDisconnectedException>();

        session.IsConnected.Should().BeFalse();

        Func<Task> actNew = async () => await session.RequestAsync<string>("NEXT_NEW_CMD", TimeSpan.FromSeconds(1));
        await actNew.Should().ThrowAsync<DeviceDisconnectedException>();
    }
}
