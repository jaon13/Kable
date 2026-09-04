namespace Kable.Tests.Cases.Codecs;

using System;
using System.Buffers;
using System.Text;
using FluentAssertions;
using Kable.Codecs;
using Kable.Exceptions;
using Xunit;

public sealed class BinaryLengthPrefixedCodecTests
{
    [Fact]
    public void TryDecode_IncompleteHeader_ReturnsFalse()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 4, isBigEndian: false);
        byte[] bytes = new byte[] { 0x10, 0x00 }; // only 2 bytes
        var seq = new ReadOnlySequence<byte>(bytes);

        bool result = codec.TryDecode(ref seq, out var msg);

        result.Should().BeFalse();
        msg.Length.Should().Be(0);
        seq.Length.Should().Be(2);
    }

    [Fact]
    public void TryDecode_IncompleteBody_ReturnsFalseAndPreservesCursor()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 4, isBigEndian: false);
        byte[] bytes = new byte[8];
        BitConverter.GetBytes(10).CopyTo(bytes, 0); // body length is 10, but only 4 body bytes follow
        Encoding.ASCII.GetBytes("ABCD").CopyTo(bytes, 4);
        var seq = new ReadOnlySequence<byte>(bytes);

        bool result = codec.TryDecode(ref seq, out var msg);

        result.Should().BeFalse();
        msg.Length.Should().Be(0);
        seq.Length.Should().Be(8);
    }

    [Fact]
    public void TryDecode_LittleEndian_CompleteFrame_DecodesSuccessfully()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 4, isBigEndian: false);
        byte[] payload = Encoding.UTF8.GetBytes("MODBUS_PACKET_DATA");
        byte[] packet = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(packet, 0);
        payload.CopyTo(packet, 4);

        var seq = new ReadOnlySequence<byte>(packet);

        bool result = codec.TryDecode(ref seq, out var msg);

        result.Should().BeTrue();
        Encoding.UTF8.GetString(msg.Span).Should().Be("MODBUS_PACKET_DATA");
        seq.Length.Should().Be(0);
    }

    [Fact]
    public void TryDecode_BigEndian_CompleteFrame_DecodesSuccessfully()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 2, isBigEndian: true);
        byte[] payload = Encoding.UTF8.GetBytes("SENSOR_PAYLOAD");
        byte[] packet = new byte[2 + payload.Length];
        short length = (short)payload.Length;
        packet[0] = (byte)((length >> 8) & 0xFF);
        packet[1] = (byte)(length & 0xFF);
        payload.CopyTo(packet, 2);

        var seq = new ReadOnlySequence<byte>(packet);

        bool result = codec.TryDecode(ref seq, out var msg);

        result.Should().BeTrue();
        Encoding.UTF8.GetString(msg.Span).Should().Be("SENSOR_PAYLOAD");
        seq.Length.Should().Be(0);
    }

    [Fact]
    public void TryDecode_ExceedingMaxFrameSize_ThrowsProtocolViolationException()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 4, isBigEndian: false, maxFrameSize: 50);
        byte[] packet = new byte[4];
        BitConverter.GetBytes(100).CopyTo(packet, 0); // 100 > 50
        var seq = new ReadOnlySequence<byte>(packet);

        Action act = () => codec.TryDecode(ref seq, out _);

        act.Should().Throw<ProtocolViolationException>()
           .WithMessage("*Frame size limit exceeded*");
    }

    [Fact]
    public void Encode_PrependsLengthHeaderCorrectly()
    {
        var codec = new BinaryLengthPrefixedCodec(headerLength: 4, isBigEndian: false);
        byte[] payload = Encoding.ASCII.GetBytes("COMMAND_DATA");

        var bufferWriter = new ArrayBufferWriter<byte>();
        codec.Encode(payload.AsMemory(), bufferWriter);

        byte[] written = bufferWriter.WrittenSpan.ToArray();
        written.Length.Should().Be(4 + payload.Length);
        int decodedLength = BitConverter.ToInt32(written, 0);
        decodedLength.Should().Be(payload.Length);
        Encoding.ASCII.GetString(written, 4, payload.Length).Should().Be("COMMAND_DATA");
    }
}
