using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SreDemo.Api.Telemetry;

public static class TelemetrySources
{
    public const string ActivitySourceName = "SreDemo.Api";
    public const string MeterName = "SreDemo.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> OperationsCreated =
        Meter.CreateCounter<long>("sredemo.operations.created");

    private static readonly Counter<long> FaultsInjected =
        Meter.CreateCounter<long>("sredemo.faults.injected");

    public static void RecordFault(string faultType, int? statusCode = null, int? delayMs = null)
    {
        var tags = new TagList { { "fault.type", faultType } };
        if (statusCode is not null)
        {
            tags.Add("http.response.status_code", statusCode);
        }

        if (delayMs is not null)
        {
            tags.Add("fault.latency_ms", delayMs);
        }

        FaultsInjected.Add(1, tags);
    }
}
