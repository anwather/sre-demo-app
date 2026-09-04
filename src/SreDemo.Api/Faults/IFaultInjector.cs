namespace SreDemo.Api.Faults;

public interface IFaultInjector
{
    int HttpErrorStatusCode { get; }

    Task ApplyLatencyAsync(CancellationToken cancellationToken);

    bool ShouldInjectHttpError();

    bool ShouldInjectStorageFailure();
}
