# 06. PerkinElmer Implementation Code

> This document provides the declarative RPC implementation for `Icpms.PerkinElmer`, demonstrating modern type-safe hardware communication over `Kable`.

---

## 1. Declarative RPC Interface (`ISyngistixRpcClient.cs`)

By declaring the typed RPC interface, the compile-time generator automatically provides proxy implementations:

```csharp
namespace Icpms.PerkinElmer.Protocol;

using Kable.Generators;

[DeviceRpcContract]
public interface ISyngistixRpcClient
{
    [RpcMethod("START_PLASMA", TimeoutSeconds = 10)]
    ValueTask<PeStatusResponse> StartPlasmaAsync(CancellationToken ct = default);

    [RpcMethod("STOP_PLASMA", TimeoutSeconds = 10)]
    ValueTask<PeStatusResponse> StopPlasmaAsync(CancellationToken ct = default);

    [RpcMethod("LOAD_METHOD", TimeoutSeconds = 5)]
    ValueTask<PeStatusResponse> LoadMethodAsync(string methodFolder, string methodName, CancellationToken ct = default);

    [RpcMethod("START_PUMP", TimeoutSeconds = 3)]
    ValueTask<PeStatusResponse> StartPumpAsync(double rpm, CancellationToken ct = default);

    [RpcMethod("START_ACQUISITION", TimeoutSeconds = 5)]
    ValueTask<PeStatusResponse> StartAcquisitionAsync(string sampleId, CancellationToken ct = default);

    [RpcUrgent("STOP_ACQUISITION")]
    ValueTask StopAcquisitionUrgentAsync();
}
```

---

## 2. Inbound Packet & Event Models (`PerkinElmerPackets.cs`)

```csharp
namespace Icpms.PerkinElmer.Protocol;

public readonly record struct PeStatusResponse(bool Success, string ErrorMessage = "");

public readonly record struct PeInstrumentStatusEvent(
    double VacuumLevelPa,
    double PlasmaPowerWatts,
    string StatusMessage
);

public readonly record struct PeAcquisitionCompletedEvent(
    string SampleId,
    IReadOnlyList<double> Intensities
);
```

---

## 3. Production Driver Orchestrator (`PerkinElmerDeviceDriver.cs`)

```csharp
namespace Icpms.PerkinElmer;

using Icpms.PerkinElmer.Protocol;

public sealed class PerkinElmerDeviceDriver : IIcpmsDriver
{
    private readonly ISyngistixRpcClient _rpc;
    private BatchRunStatus _batchRunStatus = new("NONE", "", 0, 0, false, "Idle");

    public PerkinElmerDeviceDriver(ISyngistixRpcClient rpc) => _rpc = rpc;

    public async Task<bool> IgnitePlasmaAsync(CancellationToken ct = default)
    {
        var res = await _rpc.StartPlasmaAsync(ct);
        return res.Success;
    }

    public async Task<bool> ExtinguishPlasmaAsync(CancellationToken ct = default)
    {
        var res = await _rpc.StopPlasmaAsync(ct);
        return res.Success;
    }

    public async Task<BatchRunStatus> StartBatchAsync(string batchName, IEnumerable<string> samples, CancellationToken ct = default)
    {
        var sampleList = samples.ToList();
        string firstSample = sampleList.FirstOrDefault() ?? "SMP-BLANK";

        // 3-step sequence: Load method -> Start pump -> Trigger acquisition
        await _rpc.LoadMethodAsync(@"C:\PE\Methods", "TraceMetals.mth", ct);
        await _rpc.StartPumpAsync(20.0, ct);
        await _rpc.StartAcquisitionAsync(firstSample, ct);

        _batchRunStatus = new BatchRunStatus(batchName, firstSample, 1, sampleList.Count, true, "Running");
        return _batchRunStatus;
    }

    public async Task<bool> AbortBatchAsync(CancellationToken ct = default)
    {
        await _rpc.StopAcquisitionUrgentAsync();
        _batchRunStatus = _batchRunStatus with { IsRunning = false, StatusMessage = "Aborted" };
        return true;
    }

    public Task<BatchRunStatus> GetBatchStatusAsync(CancellationToken ct = default) => Task.FromResult(_batchRunStatus);
    public Task<PlasmaState> GetPlasmaStateAsync(CancellationToken ct = default)
        => Task.FromResult(new PlasmaState(PlasmaStatus.On, 1400.0, 500.0, 1.0e-3, 19.0, "Normal"));
}
```

---

## 4. DI Registration Extensions (`PerkinElmerServiceExtensions.cs`)

```csharp
namespace Microsoft.Extensions.DependencyInjection;

using Icpms.PerkinElmer;
using Icpms.PerkinElmer.Protocol;
using Kable.Transports;

public static class PerkinElmerServiceExtensions
{
    public static IServiceCollection AddPerkinElmerDeviceDriver(
        this IServiceCollection services,
        string hostIp = "192.168.1.120",
        int tcpPort = 50051)
    {
        services.AddDeviceRpcClient<ISyngistixRpcClient>(options =>
        {
            options.UseSocket(hostIp, tcpPort);
        });

        services.AddSingleton<IIcpmsDriver, PerkinElmerDeviceDriver>();
        return services;
    }
}
```
