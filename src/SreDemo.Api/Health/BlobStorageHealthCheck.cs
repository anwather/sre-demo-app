using Microsoft.Extensions.Diagnostics.HealthChecks;
using SreDemo.Api.Operations;

namespace SreDemo.Api.Health;

public sealed class BlobStorageHealthCheck(IOperationStore operationStore) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await operationStore.IsReadyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Blob Storage container is reachable.")
            : HealthCheckResult.Unhealthy("Blob Storage container is not reachable.");
    }
}
