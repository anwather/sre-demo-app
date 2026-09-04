using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SreDemo.Api.Operations;

namespace SreDemo.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Healthz_ReturnsOkWithoutCheckingDependencies()
    {
        await using var factory = new TestApplicationFactory(storageReady: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsOkWhenStorageIsReachable()
    {
        await using var factory = new TestApplicationFactory(storageReady: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailableWhenStorageIsUnreachable()
    {
        await using var factory = new TestApplicationFactory(storageReady: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class TestApplicationFactory(bool storageReady) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:AccountUrl"] = "https://example.blob.core.windows.net",
                    ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000;" +
                        "IngestionEndpoint=https://example.invalid/"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOperationStore>();
                services.AddSingleton<IOperationStore>(new FakeOperationStore(storageReady));
            });
        }
    }

    private sealed class FakeOperationStore(bool isReady) : IOperationStore
    {
        public Task SaveAsync(StoredOperation operation, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<StoredOperation?> GetAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<StoredOperation?>(null);

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(isReady);
    }
}
