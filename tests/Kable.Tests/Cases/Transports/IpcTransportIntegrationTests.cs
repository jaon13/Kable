namespace Kable.Tests.Cases.Transports;

using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Transport.Ipc;
using Xunit;

public sealed class IpcTransportIntegrationTests
{
    [Fact]
    public async Task IpcNamedPipe_ClientServerRoundTrip_TransfersDataSeamlessly()
    {
        string pipeName = "Kable_Test_Pipe_" + Guid.NewGuid().ToString("N");
        await using var server = new IpcNamedPipeServerListener(pipeName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            await using var serverCtx = await server.AcceptAsync(cts.Token);
            var readResult = await serverCtx.Input.ReadAsync(cts.Token);
            var received = Encoding.ASCII.GetString(System.Buffers.BuffersExtensions.ToArray(readResult.Buffer));
            serverCtx.Input.AdvanceTo(readResult.Buffer.End);
            return received;
        });

        var clientFactory = new IpcNamedPipeClientFactory(pipeName);
        await using var clientCtx = await clientFactory.ConnectAsync(cts.Token);

        // Act: Client writes ASCII string
        var message = "PING_IPC_MSG\n";
        var bytes = Encoding.ASCII.GetBytes(message);
        await clientCtx.Output.WriteAsync(bytes, cts.Token);
        await clientCtx.Output.FlushAsync(cts.Token);

        var receivedText = await serverTask;

        // Assert
        receivedText.Should().Be("PING_IPC_MSG\n");
    }

    [Fact]
    public async Task KableSession_OverIpcNamedPipe_CompletesRequestAsyncSuccessfully()
    {
        string pipeName = "Kable_Session_Pipe_" + Guid.NewGuid().ToString("N");
        await using var server = new IpcNamedPipeServerListener(pipeName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var requestCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            await using var ctx = await server.AcceptAsync(cts.Token);
            while (!cts.IsCancellationRequested)
            {
                var res = await ctx.Input.ReadAsync(cts.Token);
                var buf = res.Buffer;
                if (buf.Length > 0)
                {
                    var responseBytes = Encoding.ASCII.GetBytes("STATUS_OK\n");
                    await ctx.Output.WriteAsync(responseBytes, cts.Token);
                    await ctx.Output.FlushAsync(cts.Token);
                    ctx.Input.AdvanceTo(buf.End);
                    break;
                }
                ctx.Input.AdvanceTo(buf.Start, buf.End);
                if (res.IsCompleted) break;
            }

            // Keep context alive until client completes its assertions
            await Task.WhenAny(requestCompleted.Task, Task.Delay(2000, cts.Token));
        });

        var clientFactory = new IpcNamedPipeClientFactory(pipeName);
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(clientFactory, codec);
        await session.StartAsync(cts.Token);

        var response = await session.RequestAsync<string>("GET_STATUS", TimeSpan.FromSeconds(3), cts.Token);
        response.Should().Be("STATUS_OK");

        requestCompleted.TrySetResult(true);
        await session.StopAsync();
        await serverTask;
    }
}
