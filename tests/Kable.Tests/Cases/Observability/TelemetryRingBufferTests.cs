namespace Kable.Tests.Cases;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Observability;
using Kable.Tests.Fixtures;
using NSubstitute;
using Xunit;

public class TelemetryRingBufferTests
{
    [Fact]
    public void CommObserver_OverCapacity_DropsOldestAndMaintainsLatestTelemetry()
    {
        var observer = new CommObserver(bufferCapacity: 5);

        for (int i = 0; i < 10; i++)
        {
            observer.OnPacketTrace(new PacketTraceRecord(
                DateTime.UtcNow,
                PacketDirection.Rx,
                TrafficKind.PeriodicTelemetry,
                "STREAM",
                ReadOnlyMemory<byte>.Empty,
                "TELEMETRY_" + i,
                TimeSpan.Zero));
        }

        int count = 0;
        int lastIndex = -1;
        while (observer.PeriodicStream.TryRead(out var record))
        {
            count++;
            var parts = record.ParsedText != null ? record.ParsedText.Split('_') : null;
            if (parts != null && parts.Length == 2 && int.TryParse(parts[1], out int idx))
            {
                lastIndex = idx;
            }
        }

        count.Should().Be(5);
        lastIndex.Should().Be(9);
    }

    [Fact]
    public async Task KableSession_WithCommObserver_RoutesAlarmsAndCommandsToObserver()
    {
        var mockObserver = Substitute.For<ICommObserver>();
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec, mockObserver);
        await session.StartAsync();


        await factory.Context.WriteAsciiLineAsync("$ALARM_HIGH_TEMP", 0x0A);
        await Task.Delay(50);


        var reqTask = session.RequestAsync<string>("GET_STATUS", TimeSpan.FromSeconds(2));
        await factory.Context.WriteAsciiLineAsync("STATUS_READY", 0x0A);
        await reqTask;


        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Kind == TrafficKind.SpontaneousAlarm &&
            r.ParsedText == "$ALARM_HIGH_TEMP"));

        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Kind == TrafficKind.AperiodicCommand &&
            r.ParsedText == "STATUS_READY"));
    }
}
