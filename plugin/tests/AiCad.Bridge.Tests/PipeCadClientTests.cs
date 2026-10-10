using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AiCad.Bridge;
using AiCad.Contracts;
using Xunit;

namespace AiCad.Bridge.Tests;

public sealed class PipeCadClientTests
{
    [Fact]
    public async Task CallAsync_SendsSessionTokenAndReturnsTypedResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeName = UniquePipeName();
        await using var server = CreateServer(pipeName);
        var serverTask = RespondOnceAsync(server, request =>
        {
            if (request.SessionToken != SessionToken)
            {
                return Error(request.MessageId, "BAD_TOKEN", "Session token was not forwarded.");
            }

            var payload = request.Payload.Deserialize<EchoRequest>(PlanJson.Options);
            return Success(request.MessageId, new EchoResult(payload!.Message.ToUpperInvariant()));
        }, cancellationToken);
        var client = CreateClient(pipeName);

        var result = await client.CallAsync<EchoRequest, EchoResult>(
            "echo",
            new EchoRequest("hello"),
            cancellationToken);

        Assert.Equal("HELLO", result.Message);
        var captured = await serverTask;
        Assert.Equal("echo", captured.Method);
    }

    [Fact]
    public async Task CallAsync_RejectsMismatchedMessageId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeName = UniquePipeName();
        await using var server = CreateServer(pipeName);
        var serverTask = RespondOnceAsync(
            server,
            _ => Success(Guid.NewGuid(), new EchoResult("wrong")),
            cancellationToken);
        var client = CreateClient(pipeName);

        var exception = await Assert.ThrowsAsync<CadBridgeException>(() =>
            client.CallAsync<EchoRequest, EchoResult>("echo", new EchoRequest("hello"), cancellationToken));

        Assert.Equal("PROTOCOL_MISMATCH", exception.Code);
        await serverTask;
    }

    [Fact]
    public async Task CallAsync_RejectsOversizedRequestBeforeConnecting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = CreateClient(UniquePipeName(), maxMessageBytes: 128);

        var exception = await Assert.ThrowsAsync<CadBridgeException>(() =>
            client.CallAsync<EchoRequest, EchoResult>(
                "echo",
                new EchoRequest(new string('x', 512)),
                cancellationToken));

        Assert.Equal("PROTOCOL_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task CallAsync_RejectsOversizedResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeName = UniquePipeName();
        await using var server = CreateServer(pipeName);
        var serverTask = RespondOnceAsync(server, request =>
            Success(request.MessageId, new EchoResult(new string('x', 2_000))), cancellationToken);
        var client = CreateClient(pipeName, maxMessageBytes: 512);

        var exception = await Assert.ThrowsAsync<CadBridgeException>(() =>
            client.CallAsync<EchoRequest, EchoResult>("echo", new EchoRequest("hello"), cancellationToken));

        Assert.Equal("PROTOCOL_MISMATCH", exception.Code);
        await serverTask;
    }

    [Fact]
    public async Task CallAsync_MapsInternalTimeoutToStableError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeName = UniquePipeName();
        await using var server = CreateServer(pipeName);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }, cancellationToken);
        var client = CreateClient(pipeName, timeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<CadBridgeException>(() =>
            client.CallAsync<EchoRequest, EchoResult>("echo", new EchoRequest("hello"), cancellationToken));

        Assert.Equal("TIMEOUT", exception.Code);
        await serverTask;
    }

    [Fact]
    public async Task CallAsync_PreservesServerErrorCode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeName = UniquePipeName();
        await using var server = CreateServer(pipeName);
        var serverTask = RespondOnceAsync(server, request =>
            Error(request.MessageId, "READ_ONLY_DOCUMENT", "The drawing is read-only."), cancellationToken);
        var client = CreateClient(pipeName);

        var exception = await Assert.ThrowsAsync<CadBridgeException>(() =>
            client.CallAsync<EchoRequest, EchoResult>("echo", new EchoRequest("hello"), cancellationToken));

        Assert.Equal("READ_ONLY_DOCUMENT", exception.Code);
        Assert.Equal("The drawing is read-only.", exception.Message);
        await serverTask;
    }

    private static PipeCadClient CreateClient(
        string pipeName,
        TimeSpan? timeout = null,
        int maxMessageBytes = 1024 * 1024)
    {
        var descriptor = new CadSessionDescriptor(
            Environment.ProcessId,
            pipeName,
            SessionToken,
            "25.1.60.0.0",
            DateTimeOffset.UtcNow);
        return new PipeCadClient(() => descriptor, timeout ?? TimeSpan.FromSeconds(2), maxMessageBytes);
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private static async Task<CadRpcRequest> RespondOnceAsync(
        NamedPipeServerStream server,
        Func<CadRpcRequest, CadRpcResponse> createResponse,
        CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellationToken);
        var request = JsonSerializer.Deserialize<CadRpcRequest>(line!, PlanJson.Options)!;
        var response = createResponse(request);
        var responseBytes = new UTF8Encoding(false).GetBytes(
            JsonSerializer.Serialize(response, PlanJson.Options) + "\n");
        await server.WriteAsync(responseBytes, cancellationToken);
        await server.FlushAsync(cancellationToken);
        return request;
    }

    private static CadRpcResponse Success<T>(Guid messageId, T result)
    {
        return new CadRpcResponse(
            messageId,
            true,
            JsonSerializer.SerializeToElement(result, PlanJson.Options),
            null);
    }

    private static CadRpcResponse Error(Guid messageId, string code, string message)
    {
        return new CadRpcResponse(messageId, false, null, new CadError(code, message));
    }

    private static string UniquePipeName() => $"aicad-test-{Guid.NewGuid():N}";

    private const string SessionToken = "abcdefghijklmnopqrstuvwxyz123456";

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);
}
