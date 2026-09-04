namespace Kable.Core;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Ultra-high-performance 0-GC ValueStopwatch struct (aligned with ASP.NET Core Kestrel runtime standard).
/// Guarantees zero heap allocation while providing high-resolution latency measurements across all target frameworks.
/// </summary>
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    private readonly long _startTimestamp;

    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan GetElapsedTime()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("An uninitialized, or 'default', ValueStopwatch cannot be used to get elapsed time.");
        }

#if NET8_0_OR_GREATER
        return Stopwatch.GetElapsedTime(_startTimestamp);
#else
        long end = Stopwatch.GetTimestamp();
        long timestampDelta = end - _startTimestamp;
        long ticks = (long)(timestampDelta * TimestampToTicks);
        return new TimeSpan(ticks);
#endif
    }
}
