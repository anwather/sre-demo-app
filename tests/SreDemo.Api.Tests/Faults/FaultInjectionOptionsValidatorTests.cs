using SreDemo.Api.Faults;

namespace SreDemo.Api.Tests.Faults;

public sealed class FaultInjectionOptionsValidatorTests
{
    private readonly FaultInjectionOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsDisabledDefaults()
    {
        var result = _validator.Validate(null, new FaultInjectionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsLatencyAboveBound()
    {
        var result = _validator.Validate(null, new FaultInjectionOptions
        {
            LatencyEnabled = true,
            LatencyMaxMs = FaultInjectionOptions.MaximumLatencyMs + 1
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("cannot exceed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_RejectsInvalidErrorRates(double errorRate)
    {
        var result = _validator.Validate(null, new FaultInjectionOptions
        {
            HttpErrorRate = errorRate
        });

        Assert.True(result.Failed);
    }
}
