namespace Kable.Tests;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Kable.Codecs;
using Xunit;

public class AsciiLineCodecTests
{
    [Theory]
    [InlineData("HELLO WORLD\n", 0x0A, "HELLO WORLD")]
    [InlineData("CMD_ON\r", 0x0D, "CMD_ON")]
    [InlineData("DATA_LINE\r\n", 0x0A, "DATA_LINE")]
    [InlineData("oBATCH.script\r", 0x0D, "oBATCH.script")]
    [InlineData("1234567890\n", 0x0A, "1234567890")]
    [InlineData("MULTI_CHAR_EXT_DEV_CMD\r", 0x0D, "MULTI_CHAR_EXT_DEV_CMD")]
    public void TryDecode_VariousDelimitersAndMessages_DecodesCleanly(string rawWire, byte delimiter, string expectedMessage)
    {
        // Arrange
        var codec = new AsciiLineCodec(delimiter: delimiter);
        var bytes = Encoding.ASCII.GetBytes(rawWire);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act
        var success = codec.TryDecode(ref sequence, out var decoded);

        // Assert
        success.Should().BeTrue();
        decoded.Should().Be(expectedMessage);
        sequence.Length.Should().Be(0);
    }

    [Theory]
    [InlineData("NO_DELIMITER_YET", 0x0A)]
    [InlineData("HALF_LINE", 0x0D)]
    [InlineData("", 0x0A)]
    public void TryDecode_IncompleteBuffer_ReturnsFalseWithoutConsuming(string incompleteText, byte delimiter)
    {
        // Arrange
        var codec = new AsciiLineCodec(delimiter: delimiter);
        var bytes = Encoding.ASCII.GetBytes(incompleteText);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act
        var success = codec.TryDecode(ref sequence, out var decoded);

        // Assert
        success.Should().BeFalse();
        decoded.Should().BeEmpty();
        sequence.Length.Should().Be(bytes.Length);
    }

    [Theory]
    [InlineData("$FileName,C:\\data.csv", true)]
    [InlineData("#STAT,550,1.2e-3", true)]
    [InlineData("$ALARM,PRESSURE_HIGH", true)]
    [InlineData("#TELEMETRY,OK", true)]
    [InlineData("ACK", false)]
    [InlineData("OK", false)]
    [InlineData("qSTAT", false)]
    [InlineData("oPON", false)]
    [InlineData("123_NORMAL_RESP", false)]
    public void IsAutonomousMessage_PatternMatching_IdentifiesTelemetryAndAlarms(string message, bool expectedIsAutonomous)
    {
        // Arrange
        var codec = new AsciiLineCodec();

        // Act & Assert
        codec.IsAutonomousMessage(message).Should().Be(expectedIsAutonomous);
    }

    [Theory]
    [InlineData("MSG1", "MSG2", 0x0A)]
    [InlineData("ACK", "OK", 0x0D)]
    public void TryDecode_ConsecutiveFrames_DecodesOneByOneInOrder(string first, string second, byte delimiter)
    {
        // Arrange
        var codec = new AsciiLineCodec(delimiter: delimiter);
        var combined = $"{first}{(char)delimiter}{second}{(char)delimiter}";
        var bytes = Encoding.ASCII.GetBytes(combined);
        var sequence = new ReadOnlySequence<byte>(bytes);

        // Act & Assert: First Message
        codec.TryDecode(ref sequence, out var decoded1).Should().BeTrue();
        decoded1.Should().Be(first);

        // Act & Assert: Second Message
        codec.TryDecode(ref sequence, out var decoded2).Should().BeTrue();
        decoded2.Should().Be(second);

        sequence.Length.Should().Be(0);
    }
}
