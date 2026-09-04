namespace Kable.Observability;

using System;
using System.Threading.Channels;

public enum TrafficKind
{
    AperiodicCommand,
    PeriodicTelemetry,
    SpontaneousAlarm
}

public enum PacketDirection
{
    Tx,
    Rx
}

public readonly struct PacketTraceRecord
{
    public DateTime TimestampUtc { get; }
    public PacketDirection Direction { get; }
    public TrafficKind Kind { get; }
    public string Tag { get; }
    public ReadOnlyMemory<byte> RawBytes { get; }
    public string? ParsedText { get; }
    public TimeSpan Latency { get; }

    public PacketTraceRecord(
        DateTime timestampUtc,
        PacketDirection direction,
        TrafficKind kind,
        string tag,
        ReadOnlyMemory<byte> rawBytes,
        string? parsedText,
        TimeSpan latency)
    {
        TimestampUtc = timestampUtc;
        Direction = direction;
        Kind = kind;
        Tag = tag;
        RawBytes = rawBytes;
        ParsedText = parsedText;
        Latency = latency;
    }
}

public interface ICommObserver
{
    void OnPacketTrace(in PacketTraceRecord trace);
    ChannelReader<PacketTraceRecord> PeriodicStream { get; }
    ChannelReader<PacketTraceRecord> CommandStream { get; }
    ChannelReader<PacketTraceRecord> AlarmStream { get; }
}
