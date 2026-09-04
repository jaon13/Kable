# 04. Agilent Implementation Code

> This document provides the production C# driver implementation for `Icpms.MassHunter`, demonstrating declarative RPC and Roslyn Source Generator integration over `Kable`.

---

## 1. Outbound Commands (`MassHunterCommands.cs`)

Using `[DeviceCommand]` attributes, the compile-time source generator automatically generates wire formatting (`FormatWireMessage`) and zero-allocation encoding routines:

```csharp
namespace Icpms.MassHunter.Protocol;

using Kable.Generators;

[DeviceCommand("oPON")]                     public readonly partial record struct IgnitePlasmaCommand;
[DeviceCommand("oPOFF")]                    public readonly partial record struct ExtinguishPlasmaCommand;
[DeviceCommand("oRESUME", IsUrgent = true)] public readonly partial record struct EmergencyAbortCommand;
[DeviceCommand("qSTAT")]                    public readonly partial record struct QueryInterlockStatusCommand;
[DeviceCommand("oBATCH.{ScriptPath}")]      public readonly partial record struct StartBatchScriptCommand(string ScriptPath);
[DeviceCommand("oAPPEND{SampleIndex}")]     public readonly partial record struct AppendSampleCommand(int SampleIndex);
```

---

## 2. Inbound Packets & Event Models (`MassHunterPackets.cs`)

```csharp
namespace Icpms.MassHunter.Protocol;

using Kable.Generators;

public interface IMassHunterPacket { string RawWireText { get; } }

public readonly record struct CommandAckResponse(string RawWireText, bool IsSuccess) : IMassHunterPacket;

[SpontaneousEvent("$FileName,{ResultCsvPath}")]
public readonly partial record struct MeasurementCompletedEvent(string RawWireText, string ResultCsvPath) : IMassHunterPacket;

[TelemetryEvent("#STAT,{ArgonGasPressureKpa},{ChamberVacuumPa},{RfGeneratorWatts}")]
public readonly partial record struct PlasmaTelemetryEvent(
    string RawWireText,
    double ArgonGasPressureKpa,
    double ChamberVacuumPa,
    double RfGeneratorWatts
) : IMassHunterPacket;
```

---

## 3. Protocol Codec (`MassHunterProtocolCodec.cs`)

```csharp
namespace Icpms.MassHunter.Protocol;

using System.Buffers;
using Kable.Codecs;

[GeneratedProtocolCodec(Delimiter = 0x0D /* '\r' */, SupportsCorrelationId = false)]
public sealed partial class MassHunterProtocolCodec : IProtocolCodec<IMassHunterPacket>
{
    // The source generator automatically outputs zero-allocation ReadOnlySequence<byte>
    // parsing and pattern matching for $FileName and #STAT packets.
}
```

---

## 4. Production Domain Driver (`MassHunterDeviceDriver.cs`)

```csharp
namespace Icpms.MassHunter;

using Icpms.MassHunter.Protocol;
using Kable.Engine;
using Kable.Exceptions;

public sealed class MassHunterDeviceDriver : IIcpmsDriver, IAsyncDisposable
{
    private readonly IDeviceSession<IMassHunterPacket> _commSession;
    private readonly CancellationTokenSource _driverCts = new();
    private Task? _backgroundStreamTask;

    private PlasmaStatus _plasmaStatus = PlasmaStatus.Off;
    private BatchRunStatus _batchRunStatus = new("NONE", "", 0, 0, false, "Idle");

    public event Action<string>? OnRawFrameReceived;
    public event Action<string>? OnMeasuredCsvDetected;
    public event Action<PlasmaTelemetryEvent>? OnTelemetryReceived;

    public MassHunterDeviceDriver(IDeviceSession<IMassHunterPacket> commSession)
    {
        _commSession = commSession;
    }

    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        await _commSession.StartAsync(ct);

        _backgroundStreamTask = Task.Run(async () =>
        {
            await foreach (var packet in _commSession.Stream.WithCancellation(_driverCts.Token))
            {
                OnRawFrameReceived?.Invoke(packet.RawWireText);

                switch (packet)
                {
                    case MeasurementCompletedEvent csv:
                        OnMeasuredCsvDetected?.Invoke(csv.ResultCsvPath);
                        break;

                    case PlasmaTelemetryEvent telemetry:
                        OnTelemetryReceived?.Invoke(telemetry);
                        break;
                }
            }
        }, _driverCts.Token);
    }

    public async Task<bool> IgnitePlasmaAsync(CancellationToken ct = default)
    {
        var ack = await _commSession.RequestAsync<CommandAckResponse>(
            new IgnitePlasmaCommand(), 
            timeout: TimeSpan.FromSeconds(10), 
            ct);

        if (ack.IsSuccess)
        {
            _plasmaStatus = PlasmaStatus.On;
            return true;
        }
        return false;
    }

    public async Task<bool> ExtinguishPlasmaAsync(CancellationToken ct = default)
    {
        var ack = await _commSession.RequestAsync<CommandAckResponse>(
            new ExtinguishPlasmaCommand(), 
            timeout: TimeSpan.FromSeconds(10), 
            ct);

        if (ack.IsSuccess)
        {
            _plasmaStatus = PlasmaStatus.Off;
            return true;
        }
        return false;
    }

    public async Task<BatchRunStatus> StartBatchAsync(string batchName, IEnumerable<string> samples, CancellationToken ct = default)
    {
        var sampleList = samples.ToList();
        string scriptCmd = $"BATCH.Create_{batchName}.script";

        // 1. Trigger batch script execution
        await _commSession.RequestAsync<CommandAckResponse>(
            new StartBatchScriptCommand(scriptCmd), 
            timeout: TimeSpan.FromSeconds(5), 
            ct);

        // 2. Append autosampler vials
        for (int i = 0; i < sampleList.Count; i++)
        {
            await _commSession.RequestAsync<CommandAckResponse>(
                new AppendSampleCommand(i + 1), 
                timeout: TimeSpan.FromSeconds(3), 
                ct);
        }

        _batchRunStatus = new BatchRunStatus(batchName, sampleList.FirstOrDefault() ?? "", 1, sampleList.Count, true, "Running");
        return _batchRunStatus;
    }

    public async Task<bool> AbortBatchAsync(CancellationToken ct = default)
    {
        // Out-of-band emergency stop directly bypassing queues
        await _commSession.SendUrgentAsync(new EmergencyAbortCommand());
        _batchRunStatus = _batchRunStatus with { IsRunning = false, StatusMessage = "Aborted" };
        return true;
    }

    public Task<BatchRunStatus> GetBatchStatusAsync(CancellationToken ct = default) => Task.FromResult(_batchRunStatus);
    public Task<PlasmaState> GetPlasmaStateAsync(CancellationToken ct = default) 
        => Task.FromResult(new PlasmaState(_plasmaStatus, 1400.0, 550.0, 1.2e-3, 18.5, "Normal"));

    public async ValueTask DisposeAsync()
    {
        _driverCts.Cancel();
        if (_backgroundStreamTask != null) await _backgroundStreamTask;
        await _commSession.DisposeAsync();
        _driverCts.Dispose();
    }
}
```

---

## 5. DI Service Extensions (`MassHunterServiceExtensions.cs`)

```csharp
namespace Microsoft.Extensions.DependencyInjection;

using Icpms.MassHunter;
using Icpms.MassHunter.Protocol;
using Kable.Core;
using Kable.Engine;
using Kable.Observability;
using Kable.Transports;

public static class MassHunterServiceExtensions
{
    public static IServiceCollection AddMassHunterDeviceDriver(
        this IServiceCollection services,
        string serialPort = "COM3",
        int baudRate = 9600)
    {
        services.AddSingleton<IDeviceSession<IMassHunterPacket>>(sp =>
            new KableSession<IMassHunterPacket>(
                connectionFactory: new SerialPortConnectionFactory(serialPort, baudRate),
                codec: new MassHunterProtocolCodec(),
                observer: sp.GetRequiredService<ICommObserver>()
            ));

        services.AddSingleton<IIcpmsDriver, MassHunterDeviceDriver>();
        return services;
    }
}
```
