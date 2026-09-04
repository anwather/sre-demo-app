using Microsoft.Extensions.Options;

namespace SreDemo.Api.Faults;

public sealed class FaultInjectionOptionsValidator : IValidateOptions<FaultInjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, FaultInjectionOptions options)
    {
        var failures = new List<string>();

        if (options.LatencyMinMs < 0)
        {
            failures.Add("FaultInjection:LatencyMinMs cannot be negative.");
        }

        if (options.LatencyMaxMs < options.LatencyMinMs)
        {
            failures.Add("FaultInjection:LatencyMaxMs must be greater than or equal to LatencyMinMs.");
        }

        if (options.LatencyMaxMs > FaultInjectionOptions.MaximumLatencyMs)
        {
            failures.Add($"FaultInjection:LatencyMaxMs cannot exceed {FaultInjectionOptions.MaximumLatencyMs}.");
        }

        if (options.HttpErrorRate is < 0 or > 1)
        {
            failures.Add("FaultInjection:HttpErrorRate must be between 0 and 1.");
        }

        if (options.HttpErrorStatusCode is < 500 or > 599)
        {
            failures.Add("FaultInjection:HttpErrorStatusCode must be between 500 and 599.");
        }

        if (options.StorageErrorRate is < 0 or > 1)
        {
            failures.Add("FaultInjection:StorageErrorRate must be between 0 and 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
