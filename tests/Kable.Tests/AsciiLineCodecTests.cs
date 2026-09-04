namespace Kable.Tests;

using System;
using System.Buffers;
using System.Text;
using Kable.Codecs;
using Xunit;

public class AsciiLineCodecTests
{
    [Fact]
    public void TryDecode_SingleLine_ReturnsTrueAndExtractsMessage()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var bytes = Encoding.ASCII.GetBytes("HELLO WORLD\n");
        var sequence = new ReadOnlySequence<byte>(bytes);

        var success = codec.TryDecode(ref sequence, out var message);

        Assert.True(success);
        Assert.Equal("HELLO WORLD", message);
        Assert.Equal(0, sequence.Length);
    }

    [Fact]
    public void TryDecode_IncompleteLine_ReturnsFalse()
    {
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var bytes = Encoding.ASCII.GetBytes("HELLO INCOMPLETE");
        var sequence = new ReadOnlySequence<byte>(bytes);

        var success = codec.TryDecode(ref sequence, out var message);

        Assert.False(success);
        Assert.Equal(string.Empty, message);
        Assert.Equal(bytes.Length, sequence.Length);
    }

    [Fact]
    public void IsAutonomousMessage_StartsPattern_IdentifiesCorrectly()
    {
        var codec = new AsciiLineCodec();
        Assert.True(codec.IsAutonomousMessage("$FileName,C:\\data.csv"));
        Assert.True(codec.IsAutonomousMessage("#STAT,550,1.2e-3"));
        Assert.False(codec.IsAutonomousMessage("ACK"));
        Assert.False(codec.IsAutonomousMessage("OK"));
    }
}
