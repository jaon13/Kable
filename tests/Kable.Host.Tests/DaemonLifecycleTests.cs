namespace Kable.Host.Tests;

using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Host;
using Kable.Transport.Ipc;
using Xunit;

public sealed class DaemonLifecycleTests
{
    [Fact]
    public async Task Daemon_GracefulShutdown_OnCancellation_ReleasesPipeAndCompletes()
    {
        string pipeName = "Daemon_Lifecycle_Test_" + Guid.NewGuid().ToString("N");
        var daemon = new DaemonService(pipeName);

        using var cts = new CancellationTokenSource();
        var daemonTask = Task.Run(async () => await daemon.RunAsync(cts.Token));

        // Wait brief moment for daemon listener to initialize
        await Task.Delay(50);

        // Cancel and verify shutdown completes without hang
        cts.Cancel();
        await daemonTask;

        daemon.ActiveSessionCount.Should().Be(0);
    }

    [Fact]
    public async Task Daemon_MultipleClients_EchoForwarding_MaintainsDataIntegrity()
    {
        string pipeName = "Daemon_Echo_Test_" + Guid.NewGuid().ToString("N");
        var daemon = new DaemonService(pipeName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var daemonTask = Task.Run(async () => await daemon.RunAsync(cts.Token));

        await Task.Delay(50);

        // Client 1 connects and verifies echo
        var client1Factory = new IpcNamedPipeClientFactory(pipeName);
        await using var client1Ctx = await client1Factory.ConnectAsync(cts.Token);

        byte[] payload1 = Encoding.ASCII.GetBytes("PING_FROM_CLIENT_1\n");
        await client1Ctx.Output.WriteAsync(payload1, cts.Token);
        await client1Ctx.Output.FlushAsync(cts.Token);

        var read1 = await client1Ctx.Input.ReadAsync(cts.Token);
        string echo1 = Encoding.ASCII.GetString(System.Buffers.BuffersExtensions.ToArray(read1.Buffer));
        client1Ctx.Input.AdvanceTo(read1.Buffer.End);
        echo1.Should().Be("PING_FROM_CLIENT_1\n");

        // Client 2 connects concurrently
        var client2Factory = new IpcNamedPipeClientFactory(pipeName);
        await using var client2Ctx = await client2Factory.ConnectAsync(cts.Token);

        byte[] payload2 = Encoding.ASCII.GetBytes("PING_FROM_CLIENT_2\n");
        await client2Ctx.Output.WriteAsync(payload2, cts.Token);
        await client2Ctx.Output.FlushAsync(cts.Token);

        var read2 = await client2Ctx.Input.ReadAsync(cts.Token);
        string echo2 = Encoding.ASCII.GetString(System.Buffers.BuffersExtensions.ToArray(read2.Buffer));
        client2Ctx.Input.AdvanceTo(read2.Buffer.End);
        echo2.Should().Be("PING_FROM_CLIENT_2\n");

        // Cleanup
        await client1Ctx.DisposeAsync();
        await client2Ctx.DisposeAsync();

        cts.Cancel();
        await daemonTask;
    }
}
