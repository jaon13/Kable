namespace Kable.Generators.Tests;

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Kable.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

[DeviceCommand("oPON")]
public readonly partial record struct TestIgniteCommand;

[DeviceCommand("oBATCH.{ScriptPath}")]
public readonly partial record struct TestBatchCommand(string ScriptPath);

[DeviceCommand("oAPPEND{SampleIndex}")]
public readonly partial record struct TestAppendCommand(int SampleIndex);

[DeviceCommand("oRESUME", IsUrgent = true)]
public readonly partial record struct TestEmergencyCommand;

[DeviceCommand("oVALVE.{ValveId}:{State}")]
public readonly partial record struct TestValveCommand(string ValveId, int State);

public class SourceGeneratorExecutionTests
{
    [Fact]
    public void GeneratedCommand_StaticWireTemplate_FormatsCorrectly()
    {
        var cmd = new TestIgniteCommand();
        cmd.FormatWireMessage().Should().Be("oPON");
        cmd.IsUrgent.Should().BeFalse();
        IDeviceWireCommand iface = cmd;
        iface.Should().NotBeNull();
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

    [Fact]
    public void TC_GEN_102_Generator_MultiParamRecords_InterpolatesAllParametersCorrectly()
    {
        var cmd = new TestValveCommand("V12", 1);
        cmd.FormatWireMessage().Should().Be("oVALVE.V12:1");
        cmd.IsUrgent.Should().BeFalse();
    }

    [Fact]
    public void TC_GEN_101_Generator_DriverExecution_GeneratesExpectedSourcesWithoutDiagnostics()
    {
        string userSource = @"
namespace TestApp;
using Kable.Generators;

[DeviceCommand(""SAMPLE.{Id}:{Volume}"")]
public readonly partial record struct MeasureSampleCommand(string Id, double Volume);
";

        var syntaxTree = CSharpSyntaxTree.ParseText(userSource);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)
        };

        var compilation = CSharpCompilation.Create("TestCompilation",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ProtocolSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().BeGreaterThanOrEqualTo(2); // KableAttributes.g.cs + MeasureSampleCommand.g.cs

        var generatedCode = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("MeasureSampleCommand"));

        generatedCode.Should().NotBeNull();
        generatedCode.Should().Contain("$\"SAMPLE.{Id}:{Volume}\"");
    }
}
