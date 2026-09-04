namespace Kable.Core;

using System;

public sealed class HeartbeatOptions<TMessage>
{
    public TimeSpan Interval { get; }
    public TimeSpan Timeout { get; }
    public Func<TMessage> PingFactory { get; }
    public Func<TMessage, bool>? IsPongResponse { get; }

    public HeartbeatOptions(
        TimeSpan interval,
        TimeSpan timeout,
        Func<TMessage> pingFactory,
        Func<TMessage, bool>? isPongResponse = null)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");

        Interval = interval;
        Timeout = timeout;
        PingFactory = pingFactory ?? throw new ArgumentNullException(nameof(pingFactory));
        IsPongResponse = isPongResponse;
    }
}
