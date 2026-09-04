namespace Kable.Transports;

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class TcpConnectionListener : IConnectionListener
{
    private readonly Socket _listenSocket;
    private readonly EndPoint _endPoint;
    private int _isStopped;

    public EndPoint LocalEndPoint => _listenSocket.LocalEndPoint ?? _endPoint;

    public TcpConnectionListener(IPAddress address, int port)
    {
        _endPoint = new IPEndPoint(address, port);
        _listenSocket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        _listenSocket.Bind(_endPoint);
        _listenSocket.Listen(128);
    }

    public async ValueTask<IConnectionContext> AcceptAsync(CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        var socket = await Task.Factory.FromAsync(
            _listenSocket.BeginAccept,
            _listenSocket.EndAccept,
            null).ConfigureAwait(false);
#else
        var socket = await _listenSocket.AcceptAsync(ct).ConfigureAwait(false);
#endif
        socket.NoDelay = true;
        return new TcpConnectionContext(socket);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isStopped, 1) == 0)
        {
            try { _listenSocket.Close(); } catch (Exception) { /* Socket already closed */ }
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        _listenSocket.Dispose();
#if NETSTANDARD2_0
        return new ValueTask(Task.CompletedTask);
#else
        return ValueTask.CompletedTask;
#endif
    }
}
