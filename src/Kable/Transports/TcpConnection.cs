namespace Kable.Transports;

using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class TcpConnectionContext : IConnectionContext
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private int _isDisposed;

    public string ConnectionId { get; }
    public string EndpointDescription { get; }
    public PipeReader Input { get; }
    public PipeWriter Output { get; }
    public CancellationToken ConnectionClosed => _cts.Token;

    public TcpConnectionContext(Socket socket)
    {
        _socket = socket;
        ConnectionId = Guid.NewGuid().ToString("N");
        EndpointDescription = socket.RemoteEndPoint?.ToString() ?? "TCP Unknown";

        _stream = new NetworkStream(socket, ownsSocket: false);
        Input = PipeReader.Create(_stream);
        Output = PipeWriter.Create(_stream);
    }

    public void Abort(string reason)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { _socket.Shutdown(SocketShutdown.Both); } catch (Exception) { /* Socket already aborted or forcibly disconnected by remote host */ }
            _socket.Close();
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
            _stream.Dispose();
#else
            await _stream.DisposeAsync().ConfigureAwait(false);
#endif
            _socket.Dispose();
            _cts.Dispose();
        }
    }
}

public sealed class TcpConnectionFactory : IConnectionFactory
{
    private readonly string _host;
    private readonly int _port;

    public TcpConnectionFactory(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

#if NETSTANDARD2_0
        await Task.Factory.FromAsync(
            socket.BeginConnect(_host, _port, null, null),
            socket.EndConnect).ConfigureAwait(false);
#else
        await socket.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
#endif

        return new TcpConnectionContext(socket);
    }
}
