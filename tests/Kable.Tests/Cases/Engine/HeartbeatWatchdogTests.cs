namespace Kable.Tests.Cases.Engine;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Core;
using Kable.Engine;
using Kable.Exceptions;
using Kable.Tests.Fixtures;
using Xunit;

public sealed class HeartbeatWatchdogTests
{
    [Fact]
    public async Task Session_WithHeartbeat_SendsPingPeriodically()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);

        int pingCount = 0;
        var options = new HeartbeatOptions<string>(
            interval: TimeSpan.FromMilliseconds(40),
            timeout: TimeSpan.FromSeconds(2),
            pingFactory: () =>
            {
                Interlocked.Increment(ref pingCount);
                return "PING";
            },
            isPongResponse: s => s == "PONG");

        await using var session = new KableSession<string>(factory, codec, heartbeatOptions: options);
        await session.StartAsync();

        // Server responds to pings
        var echoTask = Task.Run(async () =>
        {
            var reader = factory.Context.RemoteRead;
            for (int i = 0; i < 3; i++)
            {
                var res = await reader.ReadAsync();
                reader.AdvanceTo(res.Buffer.End);
                await factory.Context.WriteAsciiLineAsync("PONG", 0x0A);
            }
        });

        await Task.Delay(150);
        await echoTask;

        pingCount.Should().BeGreaterThanOrEqualTo(2);
        session.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Session_HeartbeatTimeout_DisconnectsSessionFailFast()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);

        var options = new HeartbeatOptions<string>(
            interval: TimeSpan.FromMilliseconds(40),
            timeout: TimeSpan.FromMilliseconds(80),
            pingFactory: () => "SILENT_PING",
            isPongResponse: s => s == "PONG");

        await using var session = new KableSession<string>(factory, codec, heartbeatOptions: options);
        await session.StartAsync();

        // Server does not respond with PONG
        await Task.Delay(200);

        // Session must be disconnected due to heartbeat timeout
        session.IsConnected.Should().BeFalse();

        // Any pending/subsequent requests should fail fast
        Func<Task> act = async () => await session.SendAsync("TEST_OFFLINE");
        await act.Should().ThrowAsync<DeviceDisconnectedException>();
    }
}
