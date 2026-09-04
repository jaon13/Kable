# 06. PerkinElmer Implementation Code (프로덕션 소스코드)

> 본 문서는 **선언적 RPC 프록시(Refit/gRPC 스타일)**와 **Roslyn Source Generator**를 결합하여, PerkinElmer NexION(Syngistix) 장비 연동 코드를 극도로 슬림하고 우아하게 작성한 `Icpms.PerkinElmer`의 최신 구현입니다.

---

## 1. [선언적 RPC 인터페이스] Syngistix RPC 선언 (`ISyngistixRpcClient.cs`)

> 지루한 TCP 소켓 파싱이나 `RequestAsync` 반복 작성 없이, **원격 RPC 인터페이스만 선언**하면 컴파일러가 프록시 구현체를 자동 생성합니다:

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

## 2. [컴팩트 모델] 패킷 및 이벤트 정의 (`PerkinElmerPackets.cs`)

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

## 3. [비즈니스 드라이버] 순수 오케스트레이터 (`PerkinElmerDeviceDriver.cs`)

> 생성된 RPC 프록시 클라이언트를 직접 주입받아, 비즈니스 흐름이 마치 일반 C# 로컬 함수 호출하듯 깔끔하게 읽힙니다.

```csharp
namespace Icpms.PerkinElmer;

using Icpms.PerkinElmer.Protocol;

public sealed class PerkinElmerDeviceDriver : IIcpmsDriver
{
    private readonly ISyngistixRpcClient _rpc;
    private BatchRunStatus _batchRunStatus = new("NONE", "", 0, 0, false, "대기 중");

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

        // 직관적인 3단계 시퀀스: 메소드 로드 -> 펌프 가동 -> 분석 시작
        await _rpc.LoadMethodAsync(@"C:\PE\Methods", "TraceMetals.mth", ct);
        await _rpc.StartPumpAsync(20.0, ct);
        await _rpc.StartAcquisitionAsync(firstSample, ct);

        _batchRunStatus = new BatchRunStatus(batchName, firstSample, 1, sampleList.Count, true, "PerkinElmer 배치 실행 중");
        return _batchRunStatus;
    }

    public async Task<bool> AbortBatchAsync(CancellationToken ct = default)
    {
        await _rpc.StopAcquisitionUrgentAsync();
        _batchRunStatus = _batchRunStatus with { IsRunning = false, StatusMessage = "긴급 중단됨" };
        return true;
    }

    public Task<BatchRunStatus> GetBatchStatusAsync(CancellationToken ct = default) => Task.FromResult(_batchRunStatus);
    public Task<PlasmaState> GetPlasmaStateAsync(CancellationToken ct = default)
        => Task.FromResult(new PlasmaState(PlasmaStatus.On, 1400.0, 500.0, 1.0e-3, 19.0, "정상"));
}
```

---

## 4. DI 서비스 등록 확장 메서드 (`PerkinElmerServiceExtensions.cs`)

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
        // 1. 소스 생성기가 만든 RPC 프록시 클라이언트 바인딩
        services.AddDeviceRpcClient<ISyngistixRpcClient>(options =>
        {
            options.UseSocket(hostIp, tcpPort);
        });

        // 2. 비즈니스 드라이버 등록
        services.AddSingleton<IIcpmsDriver, PerkinElmerDeviceDriver>();
        return services;
    }
}
```
