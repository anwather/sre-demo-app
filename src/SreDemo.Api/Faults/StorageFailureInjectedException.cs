namespace SreDemo.Api.Faults;

public sealed class StorageFailureInjectedException()
    : Exception("A Blob Storage dependency failure was injected by configuration.");
