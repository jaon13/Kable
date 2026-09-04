namespace Kable.Tests.Cases;

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Tests.Fixtures;
using Xunit;

public class ByteFragmentationTests
{
    private sealed class CustomBufferSegment : ReadOnlySequenceSegment<byte>
    {
        public CustomBufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public CustomBufferSegment SetNext(CustomBufferSegment next)
        {
            Next = next;
            next.RunningIndex = RunningIndex + Memory.Length;
            return next;
        }
    }

    [Theory]
    [InlineData(1, 0x0A)]
    [InlineData(2, 0x0D)]
    [InlineData(3, 0x0A)]
    [InlineData(7, 0x0D)]
    public async Task RequestAsync_FragmentedBytesStream_ReassemblesCompleteResponse(int chunkSize, byte delimiter)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: delimiter);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        string expectedResponse = "FRAG_RESPONSE_PAYLOAD_123456789_OK";
        byte[] payloadWithDelim = Encoding.UTF8.GetBytes(expectedResponse + (char)delimiter);

        var requestTask = session.RequestAsync<string>("GET_FRAG_DATA", TimeSpan.FromSeconds(5));
        await factory.Context.WriteFragmentedBytesAsync(payloadWithDelim, chunkSize, TimeSpan.FromMilliseconds(5));

        var actualResponse = await requestTask;
        actualResponse.Should().Be(expectedResponse);
    }

    [Theory]
    [InlineData(3, 0x0A)]
    [InlineData(5, 0x0D)]
    public async Task RequestAsync_BackToBackPackedFramesInSingleRead_HandlesEachCorrectly(int totalFrames, byte delimiter)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: delimiter);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        var sb = new StringBuilder();
        for (int i = 0; i < totalFrames; i++)
        {
            sb.Append("BATCH_FRAME_").Append(i).Append((char)delimiter);
        }
        byte[] packedBytes = Encoding.UTF8.GetBytes(sb.ToString());

        var requestTask = session.RequestAsync<string>("TRIGGER_BATCH", TimeSpan.FromSeconds(5));
        await factory.Context.RemoteWrite.WriteAsync(packedBytes);
        await factory.Context.RemoteWrite.FlushAsync();

        var firstResp = await requestTask;
        firstResp.Should().Be("BATCH_FRAME_0");

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        var streamReader = session.GetStreamAsync(cts.Token);
        int frameIndex = 1;
        await foreach (var item in streamReader)
        {
            item.Should().Be("BATCH_FRAME_" + frameIndex);
            frameIndex++;
            if (frameIndex >= totalFrames) break;
        }
        frameIndex.Should().Be(totalFrames);
    }

    [Fact]
    public void TC_COD_107_Codec_TwoByteDelimiterSplitAcrossSegments_DecodesCleanly()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A); // \n delimiter with \r preceding

        // Segment 1 ends with \r, Segment 2 starts with \n
        var seg1 = new CustomBufferSegment(Encoding.ASCII.GetBytes("PAYLOAD_TEST\r"));
        var seg2 = new CustomBufferSegment(Encoding.ASCII.GetBytes("\nREMAINING"));
        seg1.SetNext(seg2);
        var sequence = new ReadOnlySequence<byte>(seg1, 0, seg2, seg2.Memory.Length);

        bool decoded = codec.TryDecode(ref sequence, out var msg);

        decoded.Should().BeTrue();
        msg.Should().Be("PAYLOAD_TEST");
        sequence.Length.Should().Be(Encoding.ASCII.GetBytes("REMAINING").Length);
    }

    [Fact]
    public void TC_COD_108_Codec_IncompleteFrame_PreservesBufferCursorAndReturnsFalse()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        byte[] partial = Encoding.ASCII.GetBytes("INCOMPLETE_FRAME_NO_DELIMITER");
        var sequence = new ReadOnlySequence<byte>(partial);
        long initialLen = sequence.Length;

        bool decoded = codec.TryDecode(ref sequence, out var msg);

        decoded.Should().BeFalse();
        msg.Should().BeEmpty();
        sequence.Length.Should().Be(initialLen, "Cursor should not advance when frame is incomplete");
    }

    [Fact]
    public void TC_COD_201_AsciiLineCodec_EmbeddedNullBytes_DecodesPreservingBinaryContent()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        // Raw bytes with embedded \0 bytes: "HEADER\0PAYLOAD\0EXTRA\n"
        byte[] raw = new byte[] { 0x48, 0x45, 0x41, 0x44, 0x45, 0x52, 0x00, 0x50, 0x41, 0x59, 0x4C, 0x4F, 0x41, 0x44, 0x00, 0x45, 0x58, 0x54, 0x52, 0x41, 0x0A };
        var sequence = new ReadOnlySequence<byte>(raw);

        bool decoded = codec.TryDecode(ref sequence, out var msg);

        decoded.Should().BeTrue();
        msg.Length.Should().Be(20); // 20 chars without delimiter
        msg.Should().Be("HEADER\0PAYLOAD\0EXTRA");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_202_AsciiLineCodec_FrameExceedingLimit_ConsumesCorruptedFrameToPreventDeadlock()
    {
        // MaxFrameSize is 20. Corrupted frame is 30 bytes + \n, followed by normal frame "OK\n"
        var codec = new AsciiLineCodec(delimiter: 0x0A, maxFrameSize: 20);

        string corruptedPayload = new string('X', 30) + "\n";
        string validPayload = "OK\n";
        byte[] bufferBytes = Encoding.ASCII.GetBytes(corruptedPayload + validPayload);
        var sequence = new ReadOnlySequence<byte>(bufferBytes);

        // 1. First decode attempt should throw ProtocolViolationException and advance past the corrupted frame
        Action act = () => codec.TryDecode(ref sequence, out _);
        act.Should().Throw<Kable.Exceptions.ProtocolViolationException>();

        // 2. Buffer must have advanced past the corrupted frame delimiter, remaining only validPayload
        sequence.Length.Should().Be(Encoding.ASCII.GetBytes(validPayload).Length);

        // 3. Next decode attempt succeeds with "OK"
        bool secondDecoded = codec.TryDecode(ref sequence, out var validMsg);
        secondDecoded.Should().BeTrue();
        validMsg.Should().Be("OK");
        sequence.Length.Should().Be(0);
    }

    [Theory]
    [InlineData(0x00)] // Null byte delimiter
    [InlineData(0x03)] // ETX
    [InlineData(0x04)] // EOT
    public void TC_COD_203_AsciiLineCodec_CustomDelimiterZero_WorksCorrectly(byte delimiter)
    {
        var codec = new AsciiLineCodec(delimiter: delimiter);
        string payload = "DEVICE_REPORT_STATUS";
        byte[] raw = new byte[payload.Length + 1];
        Encoding.ASCII.GetBytes(payload).CopyTo(raw, 0);
        raw[payload.Length] = delimiter;

        var sequence = new ReadOnlySequence<byte>(raw);

        bool decoded = codec.TryDecode(ref sequence, out var msg);

        decoded.Should().BeTrue();
        msg.Should().Be(payload);
        sequence.Length.Should().Be(0);
    }
}