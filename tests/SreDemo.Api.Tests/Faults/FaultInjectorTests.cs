using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SreDemo.Api.Faults;

namespace SreDemo.Api.Tests.Faults;

public sealed class FaultInjectorTests
{
    [Fact]
    public async Task ApplyLatencyAsync_UsesConfiguredBound()
    {
        var options = new FaultInjectionOptions
        {
            LatencyEnabled = true,
            LatencyMinMs = 1,
            LatencyMaxMs = 1
        };
        var injector = CreateInjector(options, new FakeRandomSource());
        var started = TimeProvider.System.GetTimestamp();

        await injector.ApplyLatencyAsync(CancellationToken.None);

        Assert.True(TimeProvider.System.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void ShouldInjectHttpError_IsDisabledAtZero()
    {
        var injector = CreateInjector(
            new FaultInjectionOptions { HttpErrorRate = 0 },
            new FakeRandomSource(0));

        Assert.False(injector.ShouldInjectHttpError());
    }

    [Fact]
    public void ShouldInjectHttpError_IsGuaranteedAtOne()
    {
        var injector = CreateInjector(
            new FaultInjectionOptions { HttpErrorRate = 1 },
            new FakeRandomSource(0.99));

        Assert.True(injector.ShouldInjectHttpError());
    }

    [Fact]
    public void ShouldInjectStorageFailure_UsesConfiguredRate()
    {
        var injector = CreateInjector(
            new FaultInjectionOptions { StorageErrorRate = 0.5 },
            new FakeRandomSource(0.49));

        Assert.True(injector.ShouldInjectStorageFailure());
    }

    private static FaultInjector CreateInjector(
        FaultInjectionOptions options,
        IRandomSource random)
    {
        return new FaultInjector(
            new StaticOptionsMonitor<FaultInjectionOptions>(options),
            random,
            NullLogger<FaultInjector>.Instance);
    }

    private sealed class FakeRandomSource(double value = 0) : IRandomSource
    {
        public double NextDouble() => value;

        public int Next(int minValue, int maxValue) => minValue;
    }
}
