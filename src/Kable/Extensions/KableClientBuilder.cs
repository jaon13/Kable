namespace Kable.Extensions;

using System;
using System.IO.Ports;
using Kable.Codecs;
using Kable.Core;
using Kable.Engine;
using Kable.Observability;
using Kable.Transports;

public sealed class KableClientBuilder<TMessage>
{
    private IConnectionFactory? _factory;
    private IProtocolCodec<TMessage>? _codec;
    private ICommObserver? _observer;

    public KableClientBuilder<TMessage> UseTcp(string host, int port)
    {
        _factory = new TcpConnectionFactory(host, port);
        return this;
    }

    public KableClientBuilder<TMessage> UseSerialPort(
        string portName,
        int baudRate = 9600,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One)
    {
        _factory = new SerialPortConnectionFactory(portName, baudRate, parity, dataBits, stopBits);
        return this;
    }

    public KableClientBuilder<TMessage> UseNamedPipe(string pipeName, string serverName = ".", int timeoutMs = 5000)
    {
        _factory = new NamedPipeConnectionFactory(pipeName, serverName, timeoutMs);
        return this;
    }

    public KableClientBuilder<TMessage> UseConnectionFactory(IConnectionFactory factory)
    {
        _factory = factory;
        return this;
    }

    public KableClientBuilder<TMessage> UseCodec(IProtocolCodec<TMessage> codec)
    {
        _codec = codec;
        return this;
    }

    public KableClientBuilder<TMessage> UseObserver(ICommObserver observer)
    {
        _observer = observer;
        return this;
    }

    public IDeviceSession<TMessage> Build()
    {
        if (_factory == null)
            throw new InvalidOperationException("ConnectionFactory must be configured (e.g. UseTcp or UseSerialPort).");

        if (_codec == null)
            throw new InvalidOperationException("ProtocolCodec must be configured (e.g. UseCodec).");

        return new KableSession<TMessage>(_factory, _codec, _observer);
    }
}
