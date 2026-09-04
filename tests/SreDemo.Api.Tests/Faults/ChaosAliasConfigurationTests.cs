using Microsoft.Extensions.Configuration;
using SreDemo.Api.Faults;

namespace SreDemo.Api.Tests.Faults;

public sealed class ChaosAliasConfigurationTests
{
    [Fact]
    public void ChaosAliasesOverrideFaultOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_FAULT_LATENCY_MS"] = "1250",
                ["APP_FAULT_ERROR_RATE_PERCENT"] = "25"
            })
            .Build();
        var options = new FaultInjectionOptions();

        options.ApplyChaosAliases(configuration);

        Assert.True(options.LatencyEnabled);
        Assert.Equal(1250, options.LatencyMinMs);
        Assert.Equal(1250, options.LatencyMaxMs);
        Assert.Equal(0.25, options.HttpErrorRate);
    }
}
