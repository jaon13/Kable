namespace Kable.Tests.Cases;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    [Fact]
    public void TC_OBS_101_CommObserver_MultiThreadedBursts_MaintainsZeroLossInCommands()
    {
        // 50 concurrent tasks writing 20 records each (total 1000 records) to an observer with capacity 2000
        var observer = new CommObserver(bufferCapacity: 2000);
        int tasksCount = 50;
        int perTaskCount = 20;

        Parallel.For(0, tasksCount, taskId =>
        {
            for (int i = 0; i < perTaskCount; i++)
            {
                observer.OnPacketTrace(new PacketTraceRecord(
                    DateTime.UtcNow,
                    PacketDirection.Tx,
                    TrafficKind.AperiodicCommand,
                    "CMD",
                    ReadOnlyMemory<byte>.Empty,
                    $"CMD_{taskId}_{i}",
                    TimeSpan.Zero));
            }
        });

        int totalRead = 0;
        var received = new HashSet<string>();
        while (observer.CommandStream.TryRead(out var rec))
        {
            totalRead++;
            if (rec.ParsedText != null)
            {
                received.Add(rec.ParsedText);
            }
        }

        totalRead.Should().Be(tasksCount * perTaskCount);
        received.Count.Should().Be(tasksCount * perTaskCount);
    }
}
