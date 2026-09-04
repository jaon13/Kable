namespace Kable.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IDeviceSession<TMessage> : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }
    IAsyncEnumerable<TMessage> Stream { get; }
    ValueTask SendAsync(TMessage message, CancellationToken ct = default);
    ValueTask<TResponse> RequestAsync<TResponse>(TMessage request, TimeSpan timeout, CancellationToken ct = default);
    ValueTask SendUrgentAsync(TMessage urgentMessage);
    ValueTask StartAsync(CancellationToken ct = default);
    ValueTask StopAsync();
}
