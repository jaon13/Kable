namespace Kable.Tests.Cases;

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Core;
using Kable.Engine;
using Kable.Extensions;
using Kable.Observability;
using Kable.Transports;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class TcpListenerAndDiTests
{
    [Fact]
    public async Task TcpConnectionListener_AcceptsClientAndTransfersData()
    {
        await using var listener = new TcpConnectionListener(IPAddress.Loopback, 0);
        int port = ((IPEndPoint)listener.LocalEndPoint).Port;

        var serverTask = Task.Run(async () =>
        {
            await using var serverCtx = await listener.AcceptAsync();
            var readResult = await serverCtx.Input.ReadAsync();
            string msg = Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(readResult.Buffer));
            serverCtx.Input.AdvanceTo(readResult.Buffer.End);

            byte[] reply = Encoding.UTF8.GetBytes("SERVER_ACK:" + msg);
            await serverCtx.Output.WriteAsync(reply);
            await serverCtx.Output.FlushAsync();
            await Task.Delay(200);
        });

        var clientFactory = new TcpConnectionFactory("127.0.0.1", port);
        await using var session = new KableClientBuilder<string>()
            .UseConnectionFactory(clientFactory)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();

        var response = await session.RequestAsync<string>("HELLO_LISTENER", TimeSpan.FromSeconds(3));
        response.Should().Be("SERVER_ACK:HELLO_LISTENER");

        await serverTask;
    }

    [Fact]
    public void ServiceCollection_AddKableAndSession_ResolvesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddKable();
        services.AddKableSession<string>((builder, sp) =>
        {
            builder.UseTcp("127.0.0.1", 12345)
                   .UseCodec(new AsciiLineCodec());
        });

        using var provider = services.BuildServiceProvider();

        var observer = provider.GetService<ICommObserver>();
        observer.Should().NotBeNull();

        var session = provider.GetService<IDeviceSession<string>>();
        session.Should().NotBeNull();
    }

    [Fact]
    public void TC_GEN_103_Builder_MissingCodecOrFactory_ThrowsDescriptiveInvalidOperationException()
    {
        var builder = new KableClientBuilder<string>();

        Action actNoFactory = () => builder.Build();
        actNoFactory.Should().Throw<InvalidOperationException>()
                    .WithMessage("*ConnectionFactory must be configured*");

        Action actNoCodec = () => builder.UseTcp("127.0.0.1", 9000).Build();
        actNoCodec.Should().Throw<InvalidOperationException>()
                  .WithMessage("*ProtocolCodec must be configured*");
    }

    [Fact]
    public void TC_GEN_104_ServiceCollection_AddKableSession_ResolvesCorrectSingletonOrScoped()
    {
        var services = new ServiceCollection();
        services.AddKable();
        services.AddKableSession<string>((builder, sp) =>
        {
            builder.UseNamedPipe("di_pipe_test")
                   .UseCodec(new AsciiLineCodec());
        });

        using var provider = services.BuildServiceProvider();

        var session1 = provider.GetRequiredService<IDeviceSession<string>>();
        var session2 = provider.GetRequiredService<IDeviceSession<string>>();

        session1.Should().BeSameAs(session2, "Default AddKableSession should register session as Singleton");
    }
}
