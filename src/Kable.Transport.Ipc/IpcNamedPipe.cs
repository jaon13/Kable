namespace Kable.Transport.Ipc;

using System;
using System.IO.Pipes;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class IpcNamedPipeConnectionContext : IConnectionContext
{
    private readonly Stream _pipeStream;
    private readonly CancellationTokenSource _cts = new();
    private int _isDisposed;

    public string ConnectionId { get; }
    public string EndpointDescription { get; }
    public PipeReader Input { get; }
    public PipeWriter Output { get; }
    public CancellationToken ConnectionClosed => _cts.Token;

    public IpcNamedPipeConnectionContext(Stream pipeStream, string pipeName)
    {
        _pipeStream = pipeStream;
        ConnectionId = Guid.NewGuid().ToString("N");
        EndpointDescription = $"ipc://local/{pipeName}";

        Input = PipeReader.Create(pipeStream);
        Output = PipeWriter.Create(pipeStream);
    }

    public void Abort(string reason)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { _pipeStream.Close(); } catch (Exception) { /* Stream already closed or pipe broken by remote process */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { await Input.CompleteAsync().ConfigureAwait(false); } catch (Exception) { /* PipeReader already completed */ }
            try { await Output.CompleteAsync().ConfigureAwait(false); } catch (Exception) { /* PipeWriter already completed */ }
#if NETSTANDARD2_0
            _pipeStream.Dispose();
#else
            await _pipeStream.DisposeAsync().ConfigureAwait(false);
#endif
            _cts.Dispose();
        }
    }
}

public sealed class IpcNamedPipeClientFactory : IConnectionFactory
{
    private readonly string _pipeName;
    private readonly string _serverName;
    private readonly int _timeoutMs;

    public IpcNamedPipeClientFactory(string pipeName, string serverName = ".", int timeoutMs = 5000)
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

        return new IpcNamedPipeConnectionContext(pipeStream, _pipeName);
    }
}

public sealed class IpcNamedPipeServerListener : IConnectionListener
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _currentServerStream;
    private readonly CancellationTokenSource _cts = new();

    public IpcNamedPipeServerListener(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async ValueTask<IConnectionContext> AcceptAsync(CancellationToken ct = default)
    {
        var serverStream = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous);

        _currentServerStream = serverStream;

#if NETSTANDARD2_0
        await Task.Factory.FromAsync(
            serverStream.BeginWaitForConnection,
            serverStream.EndWaitForConnection,
            null).ConfigureAwait(false);
#else
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await serverStream.WaitForConnectionAsync(linkedCts.Token).ConfigureAwait(false);
#endif

        return new IpcNamedPipeConnectionContext(serverStream, _pipeName);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _currentServerStream?.Close(); } catch (Exception) { /* Stream already closed */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_currentServerStream != null)
        {
            try
            {
#if NETSTANDARD2_0
                _currentServerStream.Dispose();
#else
                await _currentServerStream.DisposeAsync().ConfigureAwait(false);
#endif
            }
            catch (Exception) { /* Stream already disposed */ }
        }
        _cts.Dispose();
    }
}
