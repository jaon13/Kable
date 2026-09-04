namespace Kable.Host;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Kable.Transport.Ipc;

public sealed class DaemonService
{
    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, Task> _activeSessions = new();

    public int ActiveSessionCount => _activeSessions.Count;

    public DaemonService(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var listener = new IpcNamedPipeServerListener(_pipeName);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await listener.AcceptAsync(ct).ConfigureAwait(false);

                var sessionTask = Task.Run(async () =>
                {
                    try
                    {
                        var reader = context.Input;
                        var writer = context.Output;

                        while (!ct.IsCancellationRequested && !context.ConnectionClosed.IsCancellationRequested)
                        {
                            var readResult = await reader.ReadAsync(ct).ConfigureAwait(false);
                            var buffer = readResult.Buffer;

                            if (buffer.Length > 0)
                            {
                                foreach (var segment in buffer)
                                {
                                    await writer.WriteAsync(segment, ct).ConfigureAwait(false);
                                }
                                await writer.FlushAsync(ct).ConfigureAwait(false);
                            }

                            reader.AdvanceTo(buffer.End);
                            if (readResult.IsCompleted || readResult.IsCanceled) break;
                        }
                    }
                    catch (Exception)
                    {
                        // Session faulted or client disconnected abruptly
                    }
                    finally
                    {
                        await context.DisposeAsync().ConfigureAwait(false);
                        _activeSessions.TryRemove(context.ConnectionId, out _);
                    }
                }, ct);

                _activeSessions[context.ConnectionId] = sessionTask;
            }
        }
        catch (OperationCanceledException)
        {
            if (!_activeSessions.IsEmpty)
            {
                await Task.WhenAny(Task.WhenAll(_activeSessions.Values), Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);
            }
        }
    }
}
