using System.Text.Json;

namespace SreDemo.Api.Operations;

public sealed class BlobOperationStore(
    IBlobContainerGateway gateway,
    ILogger<BlobOperationStore> logger) : IOperationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(StoredOperation operation, CancellationToken cancellationToken)
    {
        await gateway.EnsureExistsAsync(cancellationToken);
        await gateway.UploadAsync(
            GetBlobName(operation.Id),
            BinaryData.FromObjectAsJson(operation, SerializerOptions),
            "application/json",
            cancellationToken);
    }

    public async Task<StoredOperation?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var content = await gateway.DownloadAsync(GetBlobName(id), cancellationToken);
        return content?.ToObjectFromJson<StoredOperation>(SerializerOptions);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await gateway.EnsureExistsAsync(cancellationToken);
            return await gateway.ExistsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Blob Storage readiness check failed");
            return false;
        }
    }

    private static string GetBlobName(string id) => $"operations/{id}.json";
}
