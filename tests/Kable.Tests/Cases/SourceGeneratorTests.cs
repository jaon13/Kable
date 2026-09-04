namespace Kable.Tests.Cases;

using FluentAssertions;
using Kable.Generators;
using Xunit;

[DeviceCommand("oPON")]
public readonly partial record struct TestIgniteCommand;

[DeviceCommand("oBATCH.{ScriptPath}")]
public readonly partial record struct TestBatchCommand(string ScriptPath);

[DeviceCommand("oAPPEND{SampleIndex}")]
public readonly partial record struct TestAppendCommand(int SampleIndex);

[DeviceCommand("oRESUME", IsUrgent = true)]
public readonly partial record struct TestEmergencyCommand;

public class SourceGeneratorTests
{
    [Fact]
    public void GeneratedCommand_StaticWireTemplate_FormatsCorrectly()
    {
        var cmd = new TestIgniteCommand();
        cmd.FormatWireMessage().Should().Be("oPON");
        cmd.IsUrgent.Should().BeFalse();
        (cmd is IDeviceWireCommand).Should().BeTrue();
    }

    [Fact]
    public void GeneratedCommand_WithParameters_InterpolatesCorrectly()
    {
        var batchCmd = new TestBatchCommand(@"C:\scripts\batch.script");
        batchCmd.FormatWireMessage().Should().Be(@"oBATCH.C:\scripts\batch.script");

        var appendCmd = new TestAppendCommand(10);
        appendCmd.FormatWireMessage().Should().Be("oAPPEND10");
    }

    [Fact]
    public void GeneratedCommand_UrgentMarker_SetsIsUrgentTrue()
    {
        var emergencyCmd = new TestEmergencyCommand();
        emergencyCmd.FormatWireMessage().Should().Be("oRESUME");
        emergencyCmd.IsUrgent.Should().BeTrue();
    }
}
