namespace SreDemo.Api.Operations;

public sealed record CreateOperationRequest(string Message);

public sealed record StoredOperation(string Id, string Message, DateTimeOffset CreatedAt);
