using Microsoft.Extensions.Logging.Abstractions;
using SreDemo.Api.Operations;

namespace SreDemo.Api.Tests.Operations;

public sealed class BlobOperationStoreTests
{
    [Fact]
    public async Task SaveAsync_UploadsJsonToExpectedBlob()
    {
        var gateway = new FakeBlobContainerGateway();
        var store = new BlobOperationStore(gateway, NullLogger<BlobOperationStore>.Instance);
        var operation = new StoredOperation(
            "operation-1",
            "hello",
            new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));

        await store.SaveAsync(operation, CancellationToken.None);

        Assert.True(gateway.EnsureExistsCalled);
        Assert.Equal("operations/operation-1.json", gateway.UploadedBlobName);
        Assert.Equal("application/json", gateway.UploadedContentType);
        Assert.Contains("\"message\":\"hello\"", gateway.UploadedContent!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_DeserializesStoredOperation()
    {
        var gateway = new FakeBlobContainerGateway
        {
            DownloadContent = BinaryData.FromString(
                """
                {"id":"operation-2","message":"stored","createdAt":"2026-09-04T00:00:00+00:00"}
                """)
        };
        var store = new BlobOperationStore(gateway, NullLogger<BlobOperationStore>.Instance);

        var result = await store.GetAsync("operation-2", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("operation-2", result.Id);
        Assert.Equal("stored", result.Message);
    }

    [Fact]
    public async Task IsReadyAsync_ReturnsFalseWhenGatewayThrows()
    {
        var gateway = new FakeBlobContainerGateway { ThrowOnExists = true };
        var store = new BlobOperationStore(gateway, NullLogger<BlobOperationStore>.Instance);

        var result = await store.IsReadyAsync(CancellationToken.None);

        Assert.False(result);
    }

    private sealed class FakeBlobContainerGateway : IBlobContainerGateway
    {
        public bool EnsureExistsCalled { get; private set; }
        public string? UploadedBlobName { get; private set; }
        public BinaryData? UploadedContent { get; private set; }
        public string? UploadedContentType { get; private set; }
        public BinaryData? DownloadContent { get; init; }
        public bool ThrowOnExists { get; init; }

        public Task EnsureExistsAsync(CancellationToken cancellationToken)
        {
            EnsureExistsCalled = true;
            return Task.CompletedTask;
        }

        public Task UploadAsync(
            string blobName,
            BinaryData content,
            string contentType,
            CancellationToken cancellationToken)
        {
            UploadedBlobName = blobName;
            UploadedContent = content;
            UploadedContentType = contentType;
            return Task.CompletedTask;
        }

        public Task<BinaryData?> DownloadAsync(
            string blobName,
            CancellationToken cancellationToken) => Task.FromResult(DownloadContent);

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return ThrowOnExists
                ? Task.FromException<bool>(new InvalidOperationException("Unavailable"))
                : Task.FromResult(true);
        }
    }
}
