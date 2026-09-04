namespace SreDemo.Api.Tests;

public sealed class CatalogServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 9, 4, 5, 0, 0, TimeSpan.Zero);

    private readonly CatalogService _service =
        new(new FixedTimeProvider(CurrentTime));

    [Fact]
    public void GetProducts_ReturnsCatalog()
    {
        var products = _service.GetProducts();

        Assert.Equal(3, products.Count);
        Assert.Contains(products, product => product.Name == "Trail Backpack");
    }

    [Fact]
    public void CreateOrder_ReturnsCalculatedOrder()
    {
        var result = _service.CreateOrder(new CreateOrderRequest(1, 2));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Order);
        Assert.Equal(178.00m, result.Order.Total);
        Assert.Equal(CurrentTime, result.Order.CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void CreateOrder_RejectsInvalidQuantity(int quantity)
    {
        var result = _service.CreateOrder(new CreateOrderRequest(1, quantity));

        Assert.False(result.IsSuccess);
        Assert.Contains(nameof(CreateOrderRequest.Quantity), result.Errors);
    }

    [Fact]
    public void CreateOrder_RejectsUnknownProduct()
    {
        var result = _service.CreateOrder(new CreateOrderRequest(999, 1));

        Assert.False(result.IsSuccess);
        Assert.Contains(nameof(CreateOrderRequest.ProductId), result.Errors);
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
