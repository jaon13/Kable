namespace Kable.Observability.OpenTelemetry.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Kable.Observability;
using Kable.Observability.OpenTelemetry;
using Xunit;

public sealed class KableMetricsCollectorTests
{
    [Fact]
    public void OnPacketTrace_IncrementsCounter_AndRecordsLatency()
    {
        var meterListener = new MeterListener();
        long packetCount = 0;
        double recordedLatency = -1;

        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Kable.Observability")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "kable.packets.count")
            {
                packetCount += measurement;
            }
        });

        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "kable.packets.latency_ms")
            {
                recordedLatency = measurement;
            }
        });

        meterListener.Start();

        var collector = new KableMetricsCollector();

        collector.OnPacketTrace(new PacketTraceRecord(
            DateTime.UtcNow,
            PacketDirection.Tx,
            TrafficKind.AperiodicCommand,
            "TEST_TAG",
            ReadOnlyMemory<byte>.Empty,
            "REQ_PAYLOAD",
            TimeSpan.FromMilliseconds(42.5),
            LogLevel.Debug));

        meterListener.RecordObservableInstruments();

        packetCount.Should().Be(1);
        recordedLatency.Should().Be(42.5);

        meterListener.Dispose();
        collector.Dispose();
    }
}
