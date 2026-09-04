namespace SreDemo.Api.Operations;

public interface IBlobContainerGateway
{
    Task EnsureExistsAsync(CancellationToken cancellationToken);

    Task UploadAsync(
        string blobName,
        BinaryData content,
        string contentType,
        CancellationToken cancellationToken);

    Task<BinaryData?> DownloadAsync(string blobName, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(CancellationToken cancellationToken);
}
