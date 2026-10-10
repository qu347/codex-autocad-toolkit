using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiCad.Contracts;

namespace AiCad.AutoCAD;

internal sealed class PipeServerHost : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _pipeName;
    private readonly string _sessionToken;
    private readonly CadRequestQueue _queue;
    private readonly int _maxMessageBytes;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverLoop;

    public PipeServerHost(
        string pipeName,
        string sessionToken,
        CadRequestQueue queue,
        int maxMessageBytes = 1024 * 1024)
    {
        _pipeName = pipeName;
        _sessionToken = sessionToken;
        _queue = queue;
        _maxMessageBytes = maxMessageBytes;
    }

    public void Start()
    {
        _serverLoop = Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _serverLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(
            inner => inner is OperationCanceledException))
        {
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ProcessConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task ProcessConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        CadRpcRequest request;
        try
        {
            var line = await ReadBoundedLineAsync(pipe, cancellationToken).ConfigureAwait(false);
            request = JsonSerializer.Deserialize<CadRpcRequest>(line, PlanJson.Options)
                ?? throw new JsonException("Request was null.");
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidDataException)
        {
            await WriteResponseAsync(
                pipe,
                new CadRpcResponse(
                    Guid.Empty,
                    false,
                    null,
                    new CadError("PROTOCOL_MISMATCH", "The CAD request is invalid.")),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TokenMatches(request.SessionToken))
        {
            await WriteResponseAsync(
                pipe,
                new CadRpcResponse(
                    request.MessageId,
                    false,
                    null,
                    new CadError("PROTOCOL_MISMATCH", "The CAD session could not be authenticated.")),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource<CadRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new QueuedCadRequest(request, completion));
        var response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);
    }

    private bool TokenMatches(string? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        var expectedBytes = StrictUtf8.GetBytes(_sessionToken);
        var candidateBytes = StrictUtf8.GetBytes(candidate);
        return expectedBytes.Length == candidateBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
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
                throw new InvalidDataException("Request ended before a newline.");
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            var bytesToWrite = newline >= 0 ? newline : count;
            if (bytes.Length + bytesToWrite > _maxMessageBytes)
            {
                throw new InvalidDataException("Request exceeded the message limit.");
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

        return StrictUtf8.GetString(payload);
    }

    private async Task WriteResponseAsync(
        Stream stream,
        CadRpcResponse response,
        CancellationToken cancellationToken)
    {
        var bytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(response, PlanJson.Options));
        if (bytes.Length > _maxMessageBytes)
        {
            bytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(
                new CadRpcResponse(
                    response.MessageId,
                    false,
                    null,
                    new CadError("INTERNAL_ERROR", "The CAD response exceeded the message limit.")),
                PlanJson.Options));
        }

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
