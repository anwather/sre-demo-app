using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SreDemo.Api.Tests;

public sealed class ApplicationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApplicationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    public async Task HealthEndpoints_ReturnOk(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
