namespace Kable.Host;

using System;
using System.Threading;
using System.Threading.Tasks;
using Kable.Transport.Ipc;

public static class Program
{
    public static async Task Main(string[] args)
    {
        string pipeName = args.Length > 0 ? args[0] : "Kable_Default_Daemon";
        Console.WriteLine($"[Kable.Host] Starting Out-of-Process Hardware Daemon on pipe: {pipeName}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("[Kable.Host] Shutdown signal received. Performing graceful teardown...");
        };

        await using var listener = new IpcNamedPipeServerListener(pipeName);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                Console.WriteLine("[Kable.Host] Waiting for client application connection...");
                var context = await listener.AcceptAsync(cts.Token);
                Console.WriteLine($"[Kable.Host] Client connected: {context.ConnectionId} ({context.EndpointDescription})");

                // Echo / Forwarding Bridge
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var reader = context.Input;
                        var writer = context.Output;

                        while (!cts.IsCancellationRequested && !context.ConnectionClosed.IsCancellationRequested)
                        {
                            var readResult = await reader.ReadAsync(cts.Token);
                            var buffer = readResult.Buffer;

                            if (buffer.Length > 0)
                            {
                                foreach (var segment in buffer)
                                {
                                    await writer.WriteAsync(segment, cts.Token);
                                }
                                await writer.FlushAsync(cts.Token);
                            }

                            reader.AdvanceTo(buffer.End);
                            if (readResult.IsCompleted || readResult.IsCanceled) break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Kable.Host] Client session terminated: {ex.Message}");
                    }
                    finally
                    {
                        await context.DisposeAsync();
                    }
                }, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Kable.Host] Daemon stopped cleanly.");
        }
    }
}
