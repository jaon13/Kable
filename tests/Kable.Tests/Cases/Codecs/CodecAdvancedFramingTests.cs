namespace Kable.Tests.Cases;

using System;
using System.Buffers;
using System.Text;
using FluentAssertions;
using Kable.Codecs;
using Xunit;

public class CodecAdvancedFramingTests
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

    private static ReadOnlySequence<byte> CreateMultiSegmentSequence(params string[] parts)
    {
        if (parts.Length == 0) return ReadOnlySequence<byte>.Empty;

        var first = new CustomBufferSegment(Encoding.UTF8.GetBytes(parts[0]));
        var current = first;

        for (int i = 1; i < parts.Length; i++)
        {
            var next = new CustomBufferSegment(Encoding.UTF8.GetBytes(parts[i]));
            current = current.SetNext(next);
        }

        return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
    }

    [Fact]
    public void TC_COD_01_MultiSegmentSequence_CorrectlyDecodesAndReturnsPooledArray()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var sequence = CreateMultiSegmentSequence("COMMAND_PART1_", "PART2_", "PART3\n");

        sequence.IsSingleSegment.Should().BeFalse();

        bool success = codec.TryDecode(ref sequence, out var message);

        success.Should().BeTrue();
        message.Should().Be("COMMAND_PART1_PART2_PART3");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_02_DelimiterSplitAcrossSegments_SplitsCleanly()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var sequence = CreateMultiSegmentSequence("FIRST_PAYLOAD\r", "\nSECOND_PAYLOAD\r\n");

        bool success1 = codec.TryDecode(ref sequence, out var msg1);
        success1.Should().BeTrue();
        msg1.Should().Be("FIRST_PAYLOAD");

        bool success2 = codec.TryDecode(ref sequence, out var msg2);
        success2.Should().BeTrue();
        msg2.Should().Be("SECOND_PAYLOAD");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_06_Utf8MultibyteSplitAcrossSegments_DecodesWithoutLoss()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A, encoding: Encoding.UTF8);

        // UTF-8 multibyte payload split across buffer segment boundaries (Euro symbol € and accented characters)
        byte[] utf8Bytes = Encoding.UTF8.GetBytes("Data_€_Sample_Café\n");

        var seg1 = new CustomBufferSegment(utf8Bytes.AsMemory(0, 4));
        var seg2 = new CustomBufferSegment(utf8Bytes.AsMemory(4, utf8Bytes.Length - 4));
        seg1.SetNext(seg2);
        var sequence = new ReadOnlySequence<byte>(seg1, 0, seg2, seg2.Memory.Length);

        bool success = codec.TryDecode(ref sequence, out var message);

        success.Should().BeTrue();
        message.Should().Be("Data_€_Sample_Café");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_03_EmptyLinesBetweenMessages_DecodesAsEmptyString()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var sequence = CreateMultiSegmentSequence("\n", "\r\n", "VALID_LINE\n");

        // First frame: empty line
        codec.TryDecode(ref sequence, out var msg1).Should().BeTrue();
        msg1.Should().BeEmpty();

        // Second frame: empty line (\r\n)
        codec.TryDecode(ref sequence, out var msg2).Should().BeTrue();
        msg2.Should().BeEmpty();

        // Third frame: valid message
        codec.TryDecode(ref sequence, out var msg3).Should().BeTrue();
        msg3.Should().Be("VALID_LINE");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_04_LargePayload_ExceedingSingleChunk_AssemblesCorrectly()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        string largeString = new string('X', 8192) + "_END";
        var sequence = CreateMultiSegmentSequence(
            largeString.Substring(0, 3000),
            largeString.Substring(3000, 3000),
            largeString.Substring(6000) + "\n");

        bool success = codec.TryDecode(ref sequence, out var message);

        success.Should().BeTrue();
        message.Should().Be(largeString);
        sequence.Length.Should().Be(0);
    }

    [Theory]
    [InlineData("$CRITICAL_ALARM_OVERTEMP", true)]
    [InlineData("#TELEMETRY_DATA_500", true)]
    [InlineData("@USER_MSG", false)]
    [InlineData("NORMAL_RESPONSE_OK", false)]
    public void TC_COD_05_CustomAlarmPrefix_CorrectlyIdentified(string message, bool expectedAutonomous)
    {
        var codec = new AsciiLineCodec();
        codec.IsAutonomousMessage(message).Should().Be(expectedAutonomous);
    }

    [Fact]
    public void TC_COD_101_Codec_InfiniteStreamWithoutDelimiter_ThrowsProtocolViolationException()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A, maxFrameSize: 1024);
        string chunk = new string('A', 512);
        var sequence = CreateMultiSegmentSequence(chunk, chunk, "OVERFLOW");

        sequence.Length.Should().BeGreaterThan(1024);

        Action act = () => codec.TryDecode(ref sequence, out _);
        act.Should().Throw<Kable.Exceptions.ProtocolViolationException>()
           .WithMessage("*Frame size limit exceeded*");
    }

    [Fact]
    public void TC_COD_102_Codec_SingleByteSlidingWindow_ReassemblesCompleteMessage()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        string rawMessage = "STATUS_REPORT_SAMPLE_123_OK\n";
        var parts = new string[rawMessage.Length];
        for (int i = 0; i < rawMessage.Length; i++)
        {
            parts[i] = rawMessage[i].ToString();
        }

        var sequence = CreateMultiSegmentSequence(parts);
        sequence.IsSingleSegment.Should().BeFalse();

        bool success = codec.TryDecode(ref sequence, out var message);
        success.Should().BeTrue();
        message.Should().Be("STATUS_REPORT_SAMPLE_123_OK");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_103_Codec_ConsecutiveDelimiters_EmitsEmptyFramesWithoutException()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var sequence = CreateMultiSegmentSequence("\r\n\r\n\n\nFINAL\n");

        codec.TryDecode(ref sequence, out var f1).Should().BeTrue();
        f1.Should().BeEmpty();

        codec.TryDecode(ref sequence, out var f2).Should().BeTrue();
        f2.Should().BeEmpty();

        codec.TryDecode(ref sequence, out var f3).Should().BeTrue();
        f3.Should().BeEmpty();

        codec.TryDecode(ref sequence, out var f4).Should().BeTrue();
        f4.Should().BeEmpty();

        codec.TryDecode(ref sequence, out var f5).Should().BeTrue();
        f5.Should().Be("FINAL");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_104_Codec_MultiByteUtf8SplitAcrossSegments_PreservesCharacters()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A, encoding: Encoding.UTF8);
        byte[] utf8Bytes = Encoding.UTF8.GetBytes("Test_Résultat_OK\n");

        var seg1 = new CustomBufferSegment(utf8Bytes.AsMemory(0, 1));
        var seg2 = new CustomBufferSegment(utf8Bytes.AsMemory(1, 2));
        var seg3 = new CustomBufferSegment(utf8Bytes.AsMemory(3, utf8Bytes.Length - 3));
        seg1.SetNext(seg2).SetNext(seg3);

        var sequence = new ReadOnlySequence<byte>(seg1, 0, seg3, seg3.Memory.Length);

        bool success = codec.TryDecode(ref sequence, out var message);
        success.Should().BeTrue();
        message.Should().Be("Test_Résultat_OK");
        sequence.Length.Should().Be(0);
    }

    [Fact]
    public void TC_COD_105_Codec_ArrayPoolRentAndReturn_MaintainsPerfectBalance()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var sequence = CreateMultiSegmentSequence("SEGMENT1_", "SEGMENT2_", "SEGMENT3\n");

        bool success = codec.TryDecode(ref sequence, out var message);
        success.Should().BeTrue();
        message.Should().Be("SEGMENT1_SEGMENT2_SEGMENT3");
    }

    private sealed class BinaryLengthPrefixedCodec : IProtocolCodec<byte[]>
    {
        public bool SupportsCorrelationId => false;
        public bool IsAutonomousMessage(byte[] message) => false;
        public string? ExtractCorrelationId(byte[] message) => null;

        public void Encode(byte[] message, IBufferWriter<byte> output)
        {
            var span = output.GetSpan(4 + message.Length);
            BitConverter.GetBytes(message.Length).CopyTo(span);
            message.CopyTo(span.Slice(4));
            output.Advance(4 + message.Length);
        }

        public bool TryDecode(ref ReadOnlySequence<byte> buffer, out byte[] message)
        {
            if (buffer.Length < 4)
            {
                message = Array.Empty<byte>();
                return false;
            }

            Span<byte> lengthBytes = stackalloc byte[4];
            buffer.Slice(0, 4).CopyTo(lengthBytes);
            int bodyLen = BitConverter.ToInt32(lengthBytes);

            if (buffer.Length < 4 + bodyLen)
            {
                message = Array.Empty<byte>();
                return false;
            }

            var bodySeq = buffer.Slice(4, bodyLen);
            message = bodySeq.ToArray();
            buffer = buffer.Slice(buffer.GetPosition(4 + bodyLen));
            return true;
        }
    }

    [Fact]
    public void TC_COD_106_Codec_BinaryLengthPrefixedHeader_WaitsForFullBody()
    {
        var codec = new BinaryLengthPrefixedCodec();
        byte[] payload = Encoding.UTF8.GetBytes("BINARY_PAYLOAD_1234");
        byte[] fullPacket = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(fullPacket, 0);
        payload.CopyTo(fullPacket, 4);

        // Header only (4 bytes)
        var seqHeaderOnly = new ReadOnlySequence<byte>(fullPacket.AsMemory(0, 4));
        codec.TryDecode(ref seqHeaderOnly, out var msg1).Should().BeFalse();
        seqHeaderOnly.Length.Should().Be(4);

        // Partial body (4 + 5 bytes)
        var seqPartial = new ReadOnlySequence<byte>(fullPacket.AsMemory(0, 9));
        codec.TryDecode(ref seqPartial, out var msg2).Should().BeFalse();
        seqPartial.Length.Should().Be(9);

        // Complete packet
        var seqComplete = new ReadOnlySequence<byte>(fullPacket);
        codec.TryDecode(ref seqComplete, out var msg3).Should().BeTrue();
        Encoding.UTF8.GetString(msg3).Should().Be("BINARY_PAYLOAD_1234");
        seqComplete.Length.Should().Be(0);
    }
}

