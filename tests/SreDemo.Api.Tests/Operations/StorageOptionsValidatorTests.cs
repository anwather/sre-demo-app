using SreDemo.Api.Operations;

namespace SreDemo.Api.Tests.Operations;

public sealed class StorageOptionsValidatorTests
{
    private readonly StorageOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsHttpsAccountUrl()
    {
        var result = _validator.Validate(null, new StorageOptions
        {
            AccountUrl = "https://account.blob.core.windows.net"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsHttpAccountUrl()
    {
        var result = _validator.Validate(null, new StorageOptions
        {
            AccountUrl = "http://account.blob.core.windows.net"
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("HTTPS", StringComparison.Ordinal));
    }
}
