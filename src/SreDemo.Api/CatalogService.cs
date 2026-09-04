namespace SreDemo.Api;

public sealed class CatalogService(TimeProvider timeProvider)
{
    private static readonly Product[] Products =
    [
        new(1, "Trail Backpack", 89.00m),
        new(2, "Insulated Bottle", 24.50m),
        new(3, "Compact Headlamp", 39.95m)
    ];

    public IReadOnlyList<Product> GetProducts() => Products;

    public Product? GetProduct(int id) =>
        Products.SingleOrDefault(candidate => candidate.Id == id);

    public OrderCreationResult CreateOrder(CreateOrderRequest request)
    {
        var product = GetProduct(request.ProductId);
        if (product is null)
        {
            return OrderCreationResult.Failed(
                nameof(request.ProductId),
                "The selected product does not exist.");
        }

        if (request.Quantity is < 1 or > 20)
        {
            return OrderCreationResult.Failed(
                nameof(request.Quantity),
                "Quantity must be between 1 and 20.");
        }

        return OrderCreationResult.Succeeded(new Order(
            Guid.NewGuid().ToString("n"),
            product.Id,
            product.Name,
            request.Quantity,
            product.Price * request.Quantity,
            timeProvider.GetUtcNow()));
    }
}

public sealed record Product(int Id, string Name, decimal Price);

public sealed record CreateOrderRequest(int ProductId, int Quantity);

public sealed record Order(
    string Id,
    int ProductId,
    string ProductName,
    int Quantity,
    decimal Total,
    DateTimeOffset CreatedAt);

public sealed record OrderCreationResult(
    Order? Order,
    Dictionary<string, string[]> Errors)
{
    public bool IsSuccess => Order is not null;

    public static OrderCreationResult Succeeded(Order order) =>
        new(order, new Dictionary<string, string[]>());

    public static OrderCreationResult Failed(string field, string message) =>
        new(null, new Dictionary<string, string[]> { [field] = [message] });
}
