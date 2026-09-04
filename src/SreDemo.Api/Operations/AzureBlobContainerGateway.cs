using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace SreDemo.Api.Operations;

public sealed class AzureBlobContainerGateway : IBlobContainerGateway
{
    private readonly BlobContainerClient _containerClient;

    public AzureBlobContainerGateway(
        BlobServiceClient serviceClient,
        IOptions<StorageOptions> options)
    {
        _containerClient = serviceClient.GetBlobContainerClient(options.Value.ContainerName);
    }

    public async Task EnsureExistsAsync(CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);
    }

    public async Task UploadAsync(
        string blobName,
        BinaryData content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var blob = _containerClient.GetBlobClient(blobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);
    }

    public async Task<BinaryData?> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _containerClient
                .GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        var response = await _containerClient.ExistsAsync(cancellationToken);
        return response.Value;
    }
}
