namespace Kable.Transports;

using System;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class SerialPortConnectionContext : IConnectionContext
{
    private readonly SerialPort _port;
    private readonly CancellationTokenSource _cts = new();
    private int _isDisposed;

    public string ConnectionId { get; }
    public string EndpointDescription { get; }
    public PipeReader Input { get; }
    public PipeWriter Output { get; }
    public CancellationToken ConnectionClosed => _cts.Token;

    public SerialPortConnectionContext(SerialPort port)
    {
        _port = port;
        ConnectionId = Guid.NewGuid().ToString("N");
        EndpointDescription = $"Serial {port.PortName} ({port.BaudRate},{port.DataBits},{port.Parity},{port.StopBits})";

        Input = PipeReader.Create(port.BaseStream);
        Output = PipeWriter.Create(port.BaseStream);
    }

    public void Abort(string reason)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            try { _port.Close(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _cts.Cancel();
            await Input.CompleteAsync().ConfigureAwait(false);
            await Output.CompleteAsync().ConfigureAwait(false);
            _port.Dispose();
            _cts.Dispose();
        }
    }
}

public sealed class SerialPortConnectionFactory : IConnectionFactory
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;

    public SerialPortConnectionFactory(
        string portName,
        int baudRate = 9600,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One)
    {
        _portName = portName;
        _baudRate = baudRate;
        _parity = parity;
        _dataBits = dataBits;
        _stopBits = stopBits;
    }

    public ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default)
    {
        var port = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
        {
            DtrEnable = true,
            RtsEnable = true
        };

        port.Open();
        return new ValueTask<IConnectionContext>(new SerialPortConnectionContext(port));
    }
}
