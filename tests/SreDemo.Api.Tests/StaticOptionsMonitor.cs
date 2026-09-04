using Microsoft.Extensions.Options;

namespace SreDemo.Api.Tests;

internal sealed class StaticOptionsMonitor<TOptions>(TOptions options) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue => options;

    public TOptions Get(string? name) => options;

    public IDisposable OnChange(Action<TOptions, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
