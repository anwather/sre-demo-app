namespace SreDemo.Api.Faults;

public interface IRandomSource
{
    double NextDouble();

    int Next(int minValue, int maxValue);
}
