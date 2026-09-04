namespace Kable.Tests.Fixtures;

using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kable.Core;

public sealed class TestMemoryConnectionContext : IConnectionContext
{
    private readonly Pipe _inPipe = new();
    private readonly Pipe _outPipe = new();
    private readonly CancellationTokenSource _cts = new();

    public string ConnectionId { get; } = "TEST-MEM-CONN";
    public string EndpointDescription { get; } = "In-Memory Duplex Pipe";
    public PipeReader Input => _inPipe.Reader;
    public PipeWriter Output => _outPipe.Writer;
    public CancellationToken ConnectionClosed => _cts.Token;

    public PipeReader RemoteRead => _outPipe.Reader;
    public PipeWriter RemoteWrite => _inPipe.Writer;

    public void Abort(string reason)
    {
        _cts.Cancel();
        _inPipe.Writer.Complete(new OperationCanceledException(reason));
        _outPipe.Writer.Complete(new OperationCanceledException(reason));
    }

    public async Task WriteFragmentedBytesAsync(byte[] data, int chunkSize, TimeSpan delayBetweenChunks)
    {
        for (int i = 0; i < data.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, data.Length - i);
            var slice = data.AsMemory(i, len);
            await RemoteWrite.WriteAsync(slice);
            await RemoteWrite.FlushAsync();
            if (delayBetweenChunks > TimeSpan.Zero)
            {
                await Task.Delay(delayBetweenChunks);
            }
        }
    }

    public async Task WriteAsciiLineAsync(string text, byte delimiter = 0x0A)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await RemoteWrite.WriteAsync(new ReadOnlyMemory<byte>(bytes));
        await RemoteWrite.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { delimiter }));
        await RemoteWrite.FlushAsync();
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inPipe.Writer.Complete();
        _inPipe.Reader.Complete();
        _outPipe.Writer.Complete();
        _outPipe.Reader.Complete();
        _cts.Dispose();
        return default;
    }
}

public sealed class TestMemoryConnectionFactory : IConnectionFactory
{
    public TestMemoryConnectionContext Context { get; } = new();
    public ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default) => new ValueTask<IConnectionContext>(Context);
}
