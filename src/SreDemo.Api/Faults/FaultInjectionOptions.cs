namespace SreDemo.Api.Faults;

public sealed class FaultInjectionOptions
{
    public const string SectionName = "FaultInjection";
    public const int MaximumLatencyMs = 30_000;

    public bool LatencyEnabled { get; set; }

    public int LatencyMinMs { get; set; }

    public int LatencyMaxMs { get; set; }

    public double HttpErrorRate { get; set; }

    public int HttpErrorStatusCode { get; set; } = StatusCodes.Status503ServiceUnavailable;

    public double StorageErrorRate { get; set; }

    public void ApplyChaosAliases(IConfiguration configuration)
    {
        if (int.TryParse(configuration["APP_FAULT_LATENCY_MS"], out var latencyMilliseconds) &&
            latencyMilliseconds > 0)
        {
            LatencyEnabled = true;
            LatencyMinMs = latencyMilliseconds;
            LatencyMaxMs = latencyMilliseconds;
        }

        if (int.TryParse(configuration["APP_FAULT_ERROR_RATE_PERCENT"], out var errorRatePercent) &&
            errorRatePercent > 0)
        {
            HttpErrorRate = errorRatePercent / 100d;
        }
    }
}
