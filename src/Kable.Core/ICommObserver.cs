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

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public readonly struct PacketTraceRecord
{
    public DateTime TimestampUtc { get; }
    public PacketDirection Direction { get; }
    public TrafficKind Kind { get; }
    public LogLevel Level { get; }
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
        TimeSpan latency,
        LogLevel level = LogLevel.Information)
    {
        TimestampUtc = timestampUtc;
        Direction = direction;
        Kind = kind;
        Level = level;
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
