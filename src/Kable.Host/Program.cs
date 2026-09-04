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

        var daemon = new DaemonService(pipeName);
        await daemon.RunAsync(cts.Token);
        Console.WriteLine("[Kable.Host] Daemon stopped cleanly.");
    }
}
