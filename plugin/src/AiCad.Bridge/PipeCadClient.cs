using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AiCad.Contracts;

namespace AiCad.Bridge;

public sealed class PipeCadClient : ICadClient
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly Func<CadSessionDescriptor> _findSession;
    private readonly TimeSpan _timeout;
    private readonly int _maxMessageBytes;

    public PipeCadClient(
        Func<CadSessionDescriptor> findSession,
        TimeSpan timeout,
        int maxMessageBytes)
    {
        _findSession = findSession;
        _timeout = timeout;
        _maxMessageBytes = maxMessageBytes;
    }

    public async Task<TResponse> CallAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _findSession();
        var messageId = Guid.NewGuid();
        var rpcRequest = new CadRpcRequest(
            messageId,
            method,
            descriptor.SessionToken,
            JsonSerializer.SerializeToElement(request, PlanJson.Options));
        var requestBytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(rpcRequest, PlanJson.Options));
        if (requestBytes.Length > _maxMessageBytes)
        {
            throw ProtocolMismatch("The CAD request exceeds the maximum message size.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                descriptor.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(requestBytes, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync("\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);

            var responseLine = await ReadBoundedLineAsync(pipe, timeout.Token).ConfigureAwait(false);
            CadRpcResponse response;
            try
            {
                response = JsonSerializer.Deserialize<CadRpcResponse>(responseLine, PlanJson.Options)
                    ?? throw ProtocolMismatch("The CAD response was empty.");
            }
            catch (JsonException exception)
            {
                throw ProtocolMismatch("The CAD response is not valid protocol JSON.", exception);
            }

            if (response.MessageId != messageId)
            {
                throw ProtocolMismatch("The CAD response message identifier does not match the request.");
            }

            if (!response.Success)
            {
                if (response.Error is null || string.IsNullOrWhiteSpace(response.Error.Code))
                {
                    throw ProtocolMismatch("The CAD response reported failure without an error code.");
                }

                throw new CadBridgeException(response.Error.Code, response.Error.Message);
            }

            if (response.Result is null)
            {
                throw ProtocolMismatch("The CAD response reported success without a result.");
            }

            try
            {
                return response.Result.Value.Deserialize<TResponse>(PlanJson.Options)
                    ?? throw ProtocolMismatch("The CAD response result was null.");
            }
            catch (JsonException exception)
            {
                throw ProtocolMismatch("The CAD response result has an unexpected shape.", exception);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CadBridgeException("TIMEOUT", "The AutoCAD request timed out.", exception);
        }
    }

    private async Task<string> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw ProtocolMismatch("The CAD response ended before a newline was received.");
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            var bytesToWrite = newline >= 0 ? newline : count;
            if (bytes.Length + bytesToWrite > _maxMessageBytes)
            {
                throw ProtocolMismatch("The CAD response exceeds the maximum message size.");
            }

            bytes.Write(buffer, 0, bytesToWrite);
            if (newline >= 0)
            {
                break;
            }
        }

        var payload = bytes.ToArray();
        if (payload.Length > 0 && payload[^1] == (byte)'\r')
        {
            Array.Resize(ref payload, payload.Length - 1);
        }

        try
        {
            return StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException exception)
        {
            throw ProtocolMismatch("The CAD response is not valid UTF-8.", exception);
        }
    }

    private static CadBridgeException ProtocolMismatch(string message, Exception? innerException = null)
    {
        return new CadBridgeException("PROTOCOL_MISMATCH", message, innerException);
    }
}
