using System.Text.Json;

namespace AiCad.Contracts;

public sealed record CadRpcRequest(
    Guid MessageId,
    string Method,
    string SessionToken,
    JsonElement Payload);

public sealed record CadRpcResponse(
    Guid MessageId,
    bool Success,
    JsonElement? Result,
    CadError? Error);

public sealed record CadError(string Code, string Message);

public sealed record EmptyRequest;

public sealed record CadSessionDescriptor(
    int ProcessId,
    string PipeName,
    string SessionToken,
    string AutoCadVersion,
    DateTimeOffset StartedAt);

public interface ICadClient
{
    Task<TResponse> CallAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        CancellationToken cancellationToken = default);
}
