using Microsoft.Extensions.Options;
using SreDemo.Api.Telemetry;

namespace SreDemo.Api.Faults;

public sealed class FaultInjector(
    IOptionsMonitor<FaultInjectionOptions> options,
    IRandomSource random,
    ILogger<FaultInjector> logger) : IFaultInjector
{
    public int HttpErrorStatusCode => options.CurrentValue.HttpErrorStatusCode;

    public async Task ApplyLatencyAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (!current.LatencyEnabled || current.LatencyMaxMs == 0)
        {
            return;
        }

        var delayMs = current.LatencyMinMs == current.LatencyMaxMs
            ? current.LatencyMinMs
            : random.Next(current.LatencyMinMs, current.LatencyMaxMs + 1);

        if (delayMs == 0)
        {
            return;
        }

        TelemetrySources.RecordFault("latency", delayMs: delayMs);
        System.Diagnostics.Activity.Current?.AddEvent(new("fault.latency", tags: new()
        {
            ["fault.type"] = "latency",
            ["fault.latency_ms"] = delayMs
        }));
        logger.LogWarning("Injecting bounded latency of {LatencyMs} ms", delayMs);
        await Task.Delay(delayMs, cancellationToken);
    }

    public bool ShouldInjectHttpError() => IsSelected(options.CurrentValue.HttpErrorRate);

    public bool ShouldInjectStorageFailure() => IsSelected(options.CurrentValue.StorageErrorRate);

    private bool IsSelected(double rate) => rate > 0 && (rate >= 1 || random.NextDouble() < rate);
}
