using Microsoft.Extensions.Options;

namespace SreDemo.Api.Operations;

public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (!Uri.TryCreate(options.AccountUrl, UriKind.Absolute, out var accountUri))
        {
            return ValidateOptionsResult.Fail("Storage:AccountUrl must be an absolute URI.");
        }

        if (!string.Equals(accountUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Storage:AccountUrl must use HTTPS.");
        }

        return ValidateOptionsResult.Success;
    }
}
