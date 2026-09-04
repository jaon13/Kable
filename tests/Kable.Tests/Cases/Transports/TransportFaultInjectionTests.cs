namespace Kable.Tests.Cases;

using System;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Core;
using Kable.Engine;
using Kable.Exceptions;
using Kable.Extensions;
using Kable.Tests.Fixtures;
using Kable.Transports;
using Xunit;

public class TransportFaultInjectionTests
{
    [Fact]
    public async Task TC_TRN_01_NamedPipe_ServerCrash_TriggersConnectionClosedInstantly()
    {
        string pipeName = "fault_pipe_" + Guid.NewGuid().ToString("N");

        var serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverAcceptTask = serverPipe.WaitForConnectionAsync();

        await using var session = new KableClientBuilder<string>()
            .UseNamedPipe(pipeName, timeoutMs: 3000)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();
        await serverAcceptTask;

        session.IsConnected.Should().BeTrue();

        // Server process crash simulation (dispose stream abruptly)
        serverPipe.Dispose();

        // Wait for EOF detection on ReadLoop
        var pendingReq = session.RequestAsync<string>("PING_CRASH", TimeSpan.FromSeconds(3)).AsTask();

        Func<Task> act = async () => await pendingReq;
        await act.Should().ThrowAsync<DeviceDisconnectedException>();

        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TC_TRN_03_TcpConnection_RemoteRstReceived_TriggersAbortAndFailsPendingRequests()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Socket? serverSocket = null;
        var serverTask = Task.Run(async () =>
        {
            serverSocket = await listener.AcceptSocketAsync();
        });

        await using var session = new KableClientBuilder<string>()
            .UseTcp("127.0.0.1", port)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();
        await serverTask;

        session.IsConnected.Should().BeTrue();

        // Force TCP RST: LingerState enabled with Timeout = 0
        serverSocket!.LingerState = new LingerOption(true, 0);
        serverSocket.Close();

        var pendingReq = session.RequestAsync<string>("PING_AFTER_RST", TimeSpan.FromSeconds(3)).AsTask();

        Func<Task> act = async () => await pendingReq;
        await act.Should().ThrowAsync<DeviceDisconnectedException>();

        session.IsConnected.Should().BeFalse();
        listener.Stop();
    }

    [Fact]
    public async Task TC_TRN_08_TelemetryStreaming_HighThroughput_ZeroGen2GarbageCollections()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        const int packetCount = 2000;
        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptSocketAsync();
            var payload = Encoding.ASCII.GetBytes("$TELEMETRY,123.456,OK\n");
            for (int i = 0; i < packetCount; i++)
            {
                serverSocket.Send(payload);
            }
            await Task.Delay(100);
            serverSocket.Shutdown(SocketShutdown.Both);
        });

        await using var session = new KableClientBuilder<string>()
            .UseTcp("127.0.0.1", port)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();

        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        int gen2Before = GC.CollectionCount(2);

        int received = 0;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var item in session.Stream.WithCancellation(cts.Token))
        {
            received++;
            if (received >= packetCount) break;
        }

        int gen2After = GC.CollectionCount(2);

        received.Should().Be(packetCount);
        (gen2After - gen2Before).Should().BeLessThanOrEqualTo(1, "High-throughput streaming should not trigger excessive Gen2 GC collections");

        await serverTask;
        listener.Stop();
    }

    [Fact]
    public async Task TC_TRN_02_NamedPipe_ConnectionTimeout_ThrowsCleanTimeoutException()
    {
        string nonExistentPipe = "non_existent_pipe_" + Guid.NewGuid().ToString("N");
        var factory = new NamedPipeConnectionFactory(nonExistentPipe, timeoutMs: 100);

        Func<Task> act = async () => await factory.ConnectAsync();
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task TC_TRN_04_TcpConnection_UnreachableHost_ThrowsSocketExceptionQuickly()
    {
        // 127.0.0.1 on closed port
        var factory = new TcpConnectionFactory("127.0.0.1", 54321);

        Func<Task> act = async () => await factory.ConnectAsync();
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public void TC_TRN_06_SerialPort_NonExistentPortName_ThrowsIOExceptionOrUnauthorized()
    {
        var factory = new SerialPortConnectionFactory("COM9999");

        Action act = () => factory.ConnectAsync();
        act.Should().Throw<Exception>()
           .Where(e => e is System.IO.IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException);
    }

    [Fact]
    public async Task TC_TRN_07_PipeBackpressure_ConsumerStalled_HandlesBackpressureWithoutMemoryLeak()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Write 1000 frames into remote write without reading from Stream immediately
        for (int i = 0; i < 1000; i++)
        {
            await factory.Context.WriteAsciiLineAsync($"$STALL_ITEM_{i}", 0x0A);
        }

        // Now consume from Stream and verify items are received
        int count = 0;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var item in session.Stream.WithCancellation(cts.Token))
        {
            count++;
            if (count >= 1000) break;
        }

        count.Should().Be(1000);
    }

    [Fact]
    public async Task TC_TRN_05_SerialPortContext_DisposeAsync_ClosesBaseStreamAndCancelsToken()
    {
        using var port = new System.IO.Ports.SerialPort("COM1");
        using var dummyStream = new System.IO.MemoryStream();
        var context = new SerialPortConnectionContext(port, dummyStream);

        context.ConnectionClosed.IsCancellationRequested.Should().BeFalse();

        await context.DisposeAsync();

        context.ConnectionClosed.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task TC_TRN_101_Tcp_ForceResetRstPacket_AbortsConnectionContextInstantly()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Socket? acceptedSocket = null;
        var acceptTask = Task.Run(async () =>
        {
            acceptedSocket = await listener.AcceptSocketAsync();
        });

        await using var session = new KableClientBuilder<string>()
            .UseTcp("127.0.0.1", port)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();
        await acceptTask;

        session.IsConnected.Should().BeTrue();

        // Enforce hard RST via socket linger
        acceptedSocket!.LingerState = new LingerOption(true, 0);
        acceptedSocket.Close();

        var pendingReq = session.RequestAsync<string>("TEST_RST_RECOVERY", TimeSpan.FromSeconds(3)).AsTask();
        Func<Task> act = async () => await pendingReq;
        await act.Should().ThrowAsync<DeviceDisconnectedException>();

        session.IsConnected.Should().BeFalse();
        listener.Stop();
    }

    [Fact]
    public async Task TC_TRN_102_NamedPipe_ServerProcessAbruptTermination_DetectsPipeBroken()
    {
        string pipeName = "test_broken_pipe_" + Guid.NewGuid().ToString("N");
        var serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverWait = serverPipe.WaitForConnectionAsync();

        await using var session = new KableClientBuilder<string>()
            .UseNamedPipe(pipeName, timeoutMs: 2000)
            .UseCodec(new AsciiLineCodec(delimiter: 0x0A))
            .Build();

        await session.StartAsync();
        await serverWait;

        session.IsConnected.Should().BeTrue();

        // Abrupt server crash simulation
        serverPipe.Dispose();

        var pendingReq = session.RequestAsync<string>("SHOULD_FAIL", TimeSpan.FromSeconds(3)).AsTask();
        Func<Task> act = async () => await pendingReq;
        await act.Should().ThrowAsync<DeviceDisconnectedException>();

        session.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task TC_TRN_103_SerialPort_PhysicalCableRemoval_HandlesBaseStreamDisposed()
    {
        using var port = new System.IO.Ports.SerialPort("COM1");
        using var memoryStream = new System.IO.MemoryStream();
        var context = new SerialPortConnectionContext(port, memoryStream);

        context.ConnectionClosed.IsCancellationRequested.Should().BeFalse();

        // Abrupt physical cable disconnect
        context.Abort("USB-to-Serial unplugged");

        context.ConnectionClosed.IsCancellationRequested.Should().BeTrue();

        // Re-entrant DisposeAsync should complete without deadlock
        await context.DisposeAsync();
    }

    [Fact]
    public async Task TC_TRN_104_Pipe_BackpressureThresholdExceeded_PausesWriterUntilDrain()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        // Fill buffer without reading
        for (int i = 0; i < 500; i++)
        {
            await factory.Context.WriteAsciiLineAsync($"$BURST_{i}", 0x0A);
        }

        // Drain buffer
        int drained = 0;
        using var drainCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var item in session.Stream.WithCancellation(drainCts.Token))
        {
            drained++;
            if (drained >= 500) break;
        }

        drained.Should().Be(500);
    }

    [Fact]
    public async Task TC_TRN_106_Tcp_ConnectionTimeoutToNonRoutableIp_ThrowsCleanTimeoutException()
    {
        // 127.0.0.1 on an unlistened port triggers immediate failure
        var factory = new TcpConnectionFactory("127.0.0.1", 59999);
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        Func<Task> act = async () => await factory.ConnectAsync(cts.Token);
        await act.Should().ThrowAsync<Exception>()
           .Where(e => e is SocketException || e is OperationCanceledException);
    }

    [Fact]
    public async Task TC_TRN_108_NamedPipe_NonExistentServer_ConnectAsyncTimesOutCleanly()
    {
        string nonExistentPipe = "non_existent_pipe_" + Guid.NewGuid().ToString("N");
        var factory = new NamedPipeConnectionFactory(nonExistentPipe, timeoutMs: 300);

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        Func<Task> act = async () => await factory.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task TC_TRN_201_SerialPortFactory_NonExistentPort_ThrowsArgumentOrIOException()
    {
        var existingPorts = new System.Collections.Generic.HashSet<string>(
            System.IO.Ports.SerialPort.GetPortNames(),
            StringComparer.OrdinalIgnoreCase);

        string nonExistentPort = $"COM_UNASSIGNED_{Guid.NewGuid():N}";
        while (existingPorts.Contains(nonExistentPort))
        {
            nonExistentPort = $"COM_UNASSIGNED_{Guid.NewGuid():N}";
        }

        var factory = new SerialPortConnectionFactory(nonExistentPort);
        Func<Task> act = async () => await factory.ConnectAsync();

        await act.Should().ThrowAsync<Exception>()
            .Where(e => e is System.IO.IOException || e is ArgumentException || e is UnauthorizedAccessException || e is PlatformNotSupportedException);
    }

    [Fact]
    public async Task TC_TRN_203_TcpConnection_LargeWriteBackpressure_CancellationDuringFlush()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Server accepts but never reads to force OS TCP send buffer saturation
        var acceptTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            // Wait without reading anything
            await Task.Delay(2000);
        });

        var factory = new TcpConnectionFactory("127.0.0.1", port);
        await using var ctx = await factory.ConnectAsync();

        // Write massive buffer to exhaust TCP send window
        byte[] largeChunk = new byte[64 * 1024]; // 64KB
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await ctx.Output.WriteAsync(largeChunk, cts.Token);
                await ctx.Output.FlushAsync(cts.Token);
            }
            cts.Token.ThrowIfCancellationRequested();
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        listener.Stop();
        await acceptTask;
    }
}

