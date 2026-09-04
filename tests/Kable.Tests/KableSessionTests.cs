namespace Kable.Tests;

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kable.Codecs;
using Kable.Core;
using Kable.Engine;
using Kable.Exceptions;
using Xunit;

// 파이프 기반 인메모리 가상 루프백 연결
public sealed class LoopbackConnectionContext : IConnectionContext
{
    private readonly Pipe _inPipe = new();
    private readonly Pipe _outPipe = new();
    private readonly CancellationTokenSource _cts = new();

    public string ConnectionId { get; } = "LOOPBACK-01";
    public string EndpointDescription { get; } = "MemoryLoopback";
    public PipeReader Input => _inPipe.Reader;
    public PipeWriter Output => _outPipe.Writer;
    public CancellationToken ConnectionClosed => _cts.Token;

    // 테스트 헬퍼: 드라이버가 쏜 바이트를 읽거나 반대로 드라이버에게 응답을 주입하는 파이프
    public PipeReader WireRead => _outPipe.Reader;
    public PipeWriter WireWrite => _inPipe.Writer;

    public void Abort(string reason) => _cts.Cancel();

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

public sealed class LoopbackConnectionFactory : IConnectionFactory
{
    public LoopbackConnectionContext Context { get; } = new();
    public ValueTask<IConnectionContext> ConnectAsync(CancellationToken ct = default) => new(Context);
}

public class KableSessionTests
{
    [Fact]
    public async Task RequestAsync_FifoLock_GuaranteesSequentialAck()
    {
        var factory = new LoopbackConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        await session.StartAsync();

        // 1. 요청 전송 태스크 시작
        var requestTask = session.RequestAsync<string>("CMD_TEST", TimeSpan.FromSeconds(3));

        // 2. 가상 장비 쪽에서 요청 확인 후 ACK 응답 주입
        var wireResult = await factory.Context.WireRead.ReadAsync();
        var wireBytes = wireResult.Buffer.ToArray();
        var wireText = Encoding.ASCII.GetString(wireBytes);
        factory.Context.WireRead.AdvanceTo(wireResult.Buffer.End);

        Assert.Equal("CMD_TEST\n", wireText);

        // 가상 장비가 ACK\n 주입
        var ackBytes = Encoding.ASCII.GetBytes("ACK\n");
        await factory.Context.WireWrite.WriteAsync(ackBytes);

        // 3. 드라이버가 ACK를 정상 수신하는지 확인
        var response = await requestTask;
        Assert.Equal("ACK", response);
    }

    [Fact]
    public async Task RequestAsync_WhenDisconnected_TriggersFailFastImmediately()
    {
        var factory = new LoopbackConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        await session.StartAsync();

        // 요청 전송 대기 걸어둠
        var requestTask = session.RequestAsync<string>("CMD_WAIT", TimeSpan.FromSeconds(5));

        // 단선 발생! (케이블 탈락)
        factory.Context.Abort("Cable disconnected");

        // 지연 없이 Fail-Fast 예외가 즉각 던져져야 함
        await Assert.ThrowsAsync<DeviceDisconnectedException>(async () => await requestTask);
    }

    [Fact]
    public async Task RequestAsync_FirmwareSilent_ThrowsDeviceTimeoutException()
    {
        var factory = new LoopbackConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);

        await session.StartAsync();

        // 장비가 묵묵부답일 때 타임아웃 예외 격리 검증
        await Assert.ThrowsAsync<DeviceTimeoutException>(async () =>
        {
            await session.RequestAsync<string>("SILENT_CMD", TimeSpan.FromMilliseconds(200));
        });
    }
}
