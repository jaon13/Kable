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

public sealed class CommObserver : ICommObserver
{
    private readonly Channel<PacketTraceRecord> _periodicChannel;
    private readonly Channel<PacketTraceRecord> _commandChannel;
    private readonly Channel<PacketTraceRecord> _alarmChannel;

    public ChannelReader<PacketTraceRecord> PeriodicStream => _periodicChannel.Reader;
    public ChannelReader<PacketTraceRecord> CommandStream => _commandChannel.Reader;
    public ChannelReader<PacketTraceRecord> AlarmStream => _alarmChannel.Reader;

    public CommObserver(int bufferCapacity = 1000)
    {
        var options = new BoundedChannelOptions(bufferCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        };

        _periodicChannel = Channel.CreateBounded<PacketTraceRecord>(options);
        _commandChannel = Channel.CreateBounded<PacketTraceRecord>(options);
        _alarmChannel = Channel.CreateBounded<PacketTraceRecord>(options);
    }

    public void OnPacketTrace(in PacketTraceRecord trace)
    {
        switch (trace.Kind)
        {
            case TrafficKind.PeriodicTelemetry:
                _periodicChannel.Writer.TryWrite(trace);
                break;
            case TrafficKind.SpontaneousAlarm:
                _alarmChannel.Writer.TryWrite(trace);
                break;
            case TrafficKind.AperiodicCommand:
            default:
                _commandChannel.Writer.TryWrite(trace);
                break;
        }
    }
}
