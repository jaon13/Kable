namespace Kable.Core;

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

public interface IConnectionContext : IAsyncDisposable
{
    string ConnectionId { get; }
    string EndpointDescription { get; }
    PipeReader Input { get; }
    PipeWriter Output { get; }
    CancellationToken ConnectionClosed { get; }
    void Abort(string reason);
}

public interface IConnectionFactory
{
    ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default);
}

public interface IConnectionListener : IAsyncDisposable
{
    ValueTask<IConnectionContext> AcceptAsync(CancellationToken ct = default);
    void Stop();
}
