using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SreDemo.Api;
using SreDemo.Api.Health;

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

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CatalogService>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    service = "catalog-api",
    status = "running",
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}));

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/ready");

var catalog = app.MapGroup("/api/products");

catalog.MapGet("/", (CatalogService service) => Results.Ok(service.GetProducts()));

catalog.MapGet("/{id:int}", (int id, CatalogService service) =>
{
    var product = service.GetProduct(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

app.MapPost("/api/orders", (
    CreateOrderRequest request,
    CatalogService service,
    ILogger<Program> logger) =>
{
    var result = service.CreateOrder(request);
    if (!result.IsSuccess)
    {
        return Results.ValidationProblem(result.Errors);
    }

    var order = result.Order!;

    logger.LogInformation(
        "Created order {OrderId} for product {ProductId} with quantity {Quantity}",
        order.Id,
        order.ProductId,
        order.Quantity);

    return Results.Created($"/api/orders/{order.Id}", order);
});

app.Run();

public partial class Program;
