namespace Kable.Tests.Cases;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Exceptions;
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

    [Fact]
    public async Task TC_OBS_102_KableSession_LogLevelClassification_EmitsDebugForCommands_AndCriticalForUrgent()
    {
        var mockObserver = Substitute.For<ICommObserver>();
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec, mockObserver);
        await session.StartAsync();

        // 1. SendAsync -> LogLevel.Debug
        await session.SendAsync("TEST_DEBUG_SEND");
        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Level == LogLevel.Debug &&
            r.Tag == "SEND" &&
            r.ParsedText == "TEST_DEBUG_SEND"));

        // 2. SendUrgentAsync -> LogLevel.Critical
        await session.SendUrgentAsync("E_STOP_CRITICAL");
        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Level == LogLevel.Critical &&
            r.Tag == "URGENT_OOB" &&
            r.ParsedText == "E_STOP_CRITICAL"));
    }

    [Fact]
    public async Task TC_OBS_103_KableSession_TimeoutAndStreamCrash_EmitsWarningAndErrorLogLevels()
    {
        var mockObserver = Substitute.For<ICommObserver>();
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec, mockObserver);
        await session.StartAsync();

        // 1. Request Timeout -> LogLevel.Warning
        Func<Task> actTimeout = async () =>
            await session.RequestAsync<string>("SILENT_REQ", TimeSpan.FromMilliseconds(50));
        await actTimeout.Should().ThrowAsync<DeviceTimeoutException>();

        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Level == LogLevel.Warning &&
            r.Tag == "DEVICE_TIMEOUT" &&
            r.Kind == TrafficKind.SpontaneousAlarm));

        // 2. ReadLoop Stream Fault -> LogLevel.Error
        factory.Context.RemoteWrite.Complete(new System.IO.IOException("Simulated physical pipe corruption"));
        await Task.Delay(100);

        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Level == LogLevel.Error &&
            r.Tag == "READ_LOOP_FAULT" &&
            r.Kind == TrafficKind.SpontaneousAlarm));
    }

    private sealed class FaultyAutonomousCodec : IProtocolCodec<string>
    {
        public bool SupportsCorrelationId => false;
        public string? ExtractCorrelationId(string message) => null;
        public void Encode(string message, System.Buffers.IBufferWriter<byte> output) { }

        public bool TryDecode(ref System.Buffers.ReadOnlySequence<byte> buffer, out string message)
        {
            if (buffer.Length > 0)
            {
                message = "FAULTY_MESSAGE";
                buffer = buffer.Slice(buffer.End);
                return true;
            }
            message = string.Empty;
            return false;
        }

        public bool IsAutonomousMessage(string message)
        {
            if (message == "FAULTY_MESSAGE")
            {
                throw new InvalidOperationException("Unexpected bug in autonomous message parser");
            }
            return false;
        }
    }

    [Fact]
    public async Task TC_OBS_104_KableSession_DispatchLoopException_LogsErrorAndDoesNotHangEngine()
    {
        var faultCodec = new FaultyAutonomousCodec();
        var mockObserver = Substitute.For<ICommObserver>();
        var factory = new TestMemoryConnectionFactory();
        await using var session = new KableSession<string>(factory, faultCodec, mockObserver);
        await session.StartAsync();

        // Write raw byte to trigger ReadLoop -> enqueue -> DispatchLoop
        await factory.Context.RemoteWrite.WriteAsync(new byte[] { 0x01 });
        await factory.Context.RemoteWrite.FlushAsync();
        await Task.Delay(150);

        // Assert: DISPATCH_MESSAGE_FAULT was recorded with LogLevel.Error and did not kill the whole process
        mockObserver.Received().OnPacketTrace(Arg.Is<PacketTraceRecord>(r =>
            r.Level == LogLevel.Error &&
            r.Tag == "DISPATCH_MESSAGE_FAULT" &&
            r.ParsedText!.Contains("InvalidOperationException")));

        // Verify session can still be stopped cleanly
        await session.StopAsync();
    }
}
