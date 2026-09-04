namespace SreDemo.Api.Faults;

public sealed class SystemRandomSource : IRandomSource
{
    public double NextDouble() => Random.Shared.NextDouble();

    public int Next(int minValue, int maxValue) => Random.Shared.Next(minValue, maxValue);
}
