namespace Kable.Tests.Cases;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Extensions;
using Kable.Transports;
using Xunit;

public class TransportAndBuilderTests
{
    [Fact]
    public async Task TcpTransport_LoopbackClientAndServer_TransmitsAndReceivesBytesCorrectly()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int assignedPort = ((IPEndPoint)listener.LocalEndpoint).Port;


        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptSocketAsync();
            var buffer = new byte[128];
            int read = serverSocket.Receive(buffer);
            string receivedCmd = Encoding.UTF8.GetString(buffer, 0, read);

            serverSocket.Send(Encoding.UTF8.GetBytes("ECHO_" + receivedCmd));
            await Task.Delay(200);
            serverSocket.Shutdown(SocketShutdown.Both);
        });


        var clientFactory = new TcpConnectionFactory("127.0.0.1", assignedPort);
        await using var session = new KableClientBuilder<string>()
            .UseConnectionFactory(clientFactory)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();


        var response = await session.RequestAsync<string>("PING", TimeSpan.FromSeconds(3));

        response.Should().Be("ECHO_PING");
        session.IsConnected.Should().BeTrue();

        await serverTask;
        listener.Stop();
    }


    [Fact]
    public void KableClientBuilder_MissingFactoryOrCodec_ThrowsInvalidOperationException()
    {
        var builder = new KableClientBuilder<string>();

        Action act1 = () => builder.Build();
        act1.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionFactory must be configured*");

        Action act2 = () => builder.UseTcp("127.0.0.1", 9000).Build();
        act2.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProtocolCodec must be configured*");
    }
}
