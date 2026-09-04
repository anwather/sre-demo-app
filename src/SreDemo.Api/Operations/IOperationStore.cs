namespace SreDemo.Api.Operations;

public interface IOperationStore
{
    Task SaveAsync(StoredOperation operation, CancellationToken cancellationToken);

    Task<StoredOperation?> GetAsync(string id, CancellationToken cancellationToken);

    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
