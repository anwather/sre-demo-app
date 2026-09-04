using System.ComponentModel.DataAnnotations;

namespace SreDemo.Api.Operations;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    [Url]
    public string AccountUrl { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$")]
    public string ContainerName { get; init; } = "operations";

    [Range(0, 10)]
    public int MaxRetries { get; init; } = 3;

    [Range(1, 5_000)]
    public int RetryDelayMs { get; init; } = 200;

    [Range(1, 60)]
    public int NetworkTimeoutSeconds { get; init; } = 10;
}
