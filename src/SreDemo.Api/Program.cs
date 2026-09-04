using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SreDemo.Api;
using SreDemo.Api.Faults;
using SreDemo.Api.Health;
using SreDemo.Api.Operations;
using SreDemo.Api.Telemetry;

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    Environment.ExitCode = await ContainerHealthProbe.CheckAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

builder.Services
    .AddOptions<FaultInjectionOptions>()
    .Bind(builder.Configuration.GetSection(FaultInjectionOptions.SectionName))
    .PostConfigure(options => options.ApplyChaosAliases(builder.Configuration))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FaultInjectionOptions>, FaultInjectionOptionsValidator>();

builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var clientOptions = new BlobClientOptions
    {
        Retry =
        {
            MaxRetries = options.MaxRetries,
            Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
            MaxDelay = TimeSpan.FromSeconds(5),
            NetworkTimeout = TimeSpan.FromSeconds(options.NetworkTimeoutSeconds)
        }
    };

    return new BlobServiceClient(
        new Uri(options.AccountUrl, UriKind.Absolute),
        sp.GetRequiredService<TokenCredential>(),
        clientOptions);
});
builder.Services.AddSingleton<IBlobContainerGateway, AzureBlobContainerGateway>();
builder.Services.AddSingleton<IOperationStore, BlobOperationStore>();
builder.Services.AddSingleton<IRandomSource, SystemRandomSource>();
builder.Services.AddSingleton<IFaultInjector, FaultInjector>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHealthChecks()
    .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready"]);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(TelemetrySources.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(TelemetrySources.MeterName))
    .UseAzureMonitor();

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

var operations = app.MapGroup("/api/operations");

operations.MapPost("/", async (
    CreateOperationRequest request,
    IOperationStore store,
    IFaultInjector faultInjector,
    TimeProvider timeProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    using var activity = TelemetrySources.ActivitySource.StartActivity("operations.create");
    await faultInjector.ApplyLatencyAsync(cancellationToken);

    if (faultInjector.ShouldInjectHttpError())
    {
        var statusCode = faultInjector.HttpErrorStatusCode;
        TelemetrySources.RecordFault("http", statusCode);
        activity?.AddEvent(new("fault.http", tags: new()
        {
            ["fault.type"] = "http",
            ["http.response.status_code"] = statusCode
        }));
        logger.LogWarning("Injected HTTP fault with status code {StatusCode}", statusCode);

        return Results.Problem(
            statusCode: statusCode,
            title: "Injected HTTP failure",
            detail: "The demo fault-injection configuration generated this response.");
    }

    if (faultInjector.ShouldInjectStorageFailure())
    {
        TelemetrySources.RecordFault("storage");
        activity?.AddEvent(new("fault.storage", tags: new() { ["fault.type"] = "storage" }));
        throw new StorageFailureInjectedException();
    }

    var operation = new StoredOperation(
        Guid.NewGuid().ToString("n"),
        request.Message.Trim(),
        timeProvider.GetUtcNow());

    await store.SaveAsync(operation, cancellationToken);
    TelemetrySources.OperationsCreated.Add(1);
    activity?.SetTag("operation.id", operation.Id);
    logger.LogInformation(
        "Stored operation {OperationId} in Blob Storage at {CreatedAt}",
        operation.Id,
        operation.CreatedAt);

    return Results.Created($"/api/operations/{operation.Id}", operation);
})
.AddEndpointFilter(async (context, next) =>
{
    var request = context.GetArgument<CreateOperationRequest>(0);
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Message)] = ["Message is required."]
        });
    }

    if (request.Message.Length > 2048)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Message)] = ["Message must be 2,048 characters or fewer."]
        });
    }

    return await next(context);
});

operations.MapGet("/{id}", async (
    string id,
    IOperationStore store,
    IFaultInjector faultInjector,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    using var activity = TelemetrySources.ActivitySource.StartActivity("operations.get");
    await faultInjector.ApplyLatencyAsync(cancellationToken);

    if (faultInjector.ShouldInjectHttpError())
    {
        var statusCode = faultInjector.HttpErrorStatusCode;
        TelemetrySources.RecordFault("http", statusCode);
        activity?.AddEvent(new("fault.http", tags: new()
        {
            ["fault.type"] = "http",
            ["http.response.status_code"] = statusCode
        }));
        logger.LogWarning("Injected HTTP fault with status code {StatusCode}", statusCode);

        return Results.Problem(
            statusCode: statusCode,
            title: "Injected HTTP failure",
            detail: "The demo fault-injection configuration generated this response.");
    }

    if (faultInjector.ShouldInjectStorageFailure())
    {
        TelemetrySources.RecordFault("storage");
        activity?.AddEvent(new("fault.storage", tags: new() { ["fault.type"] = "storage" }));
        throw new StorageFailureInjectedException();
    }

    if (id.Length is < 1 or > 128 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
    {
        return Results.BadRequest();
    }

    var operation = await store.GetAsync(id, cancellationToken);
    if (operation is null)
    {
        logger.LogInformation("Operation {OperationId} was not found", id);
        return Results.NotFound();
    }

    activity?.SetTag("operation.id", operation.Id);
    return Results.Ok(operation);
});

app.Run();

public partial class Program;
