namespace Kable.Transports;

using System;
using System.IO.Pipes;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class NamedPipeConnectionContext : IConnectionContext
{
    private readonly NamedPipeClientStream _pipeStream;
    private readonly CancellationTokenSource _cts = new();
    private int _isDisposed;

    public string ConnectionId { get; }
    public string EndpointDescription { get; }
    public PipeReader Input { get; }
    public PipeWriter Output { get; }
    public CancellationToken ConnectionClosed => _cts.Token;

    public NamedPipeConnectionContext(NamedPipeClientStream pipeStream, string pipeName)
    {
        _pipeStream = pipeStream;
        ConnectionId = Guid.NewGuid().ToString("N");
        EndpointDescription = $"NamedPipe://localhost/{pipeName}";

        Input = PipeReader.Create(pipeStream);
        Output = PipeWriter.Create(pipeStream);
    }

    public void Abort(string reason)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { _pipeStream.Close(); } catch (Exception) { /* Pipe stream already broken or disconnected */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { await Input.CompleteAsync().ConfigureAwait(false); } catch (Exception) { /* Pipeline reader already completed */ }
            try { await Output.CompleteAsync().ConfigureAwait(false); } catch (Exception) { /* Pipeline writer already completed */ }
#if NETSTANDARD2_0
            _pipeStream.Dispose();
#else
            await _pipeStream.DisposeAsync().ConfigureAwait(false);
#endif
            _cts.Dispose();
        }
    }
}

public sealed class NamedPipeConnectionFactory : IConnectionFactory
{
    private readonly string _pipeName;
    private readonly string _serverName;
    private readonly int _timeoutMs;

    public NamedPipeConnectionFactory(string pipeName, string serverName = ".", int timeoutMs = 5000)
    {
        _pipeName = pipeName;
        _serverName = serverName;
        _timeoutMs = timeoutMs;
    }

    public async ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default)
    {
        var pipeStream = new NamedPipeClientStream(
            _serverName,
            _pipeName,
            PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);

#if NETSTANDARD2_0
        pipeStream.Connect(_timeoutMs);
        await Task.CompletedTask;
#else
        await pipeStream.ConnectAsync(_timeoutMs, ct).ConfigureAwait(false);
#endif

        return new NamedPipeConnectionContext(pipeStream, _pipeName);
    }
}
