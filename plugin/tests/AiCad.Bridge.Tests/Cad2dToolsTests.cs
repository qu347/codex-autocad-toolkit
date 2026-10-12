using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ModelContextProtocol;
using AiCad.Contracts;
using Xunit;

namespace AiCad.Bridge.Tests;

public sealed class Cad2dToolsTests
{
    private const string ValidJson = """
        {"schemaVersion":"2d-1.0","requestId":"55555555-5555-5555-5555-555555555555",
         "documentFingerprint":"fingerprint-1","unit":"millimeter",
         "layers":[],"entities":[{"type":"circle","id":"bore","layer":"0","center":{"x":0,"y":0},"radius":30}]}
        """;

    [Fact]
    public async Task ValidDrawing_SendsOneCreateRequest()
    {
        var client = new RecordingClient();
        var result = await Invoke<CreateResult>(client, "Draw2dAsync", ValidJson, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Single(client.Methods, m => m == "draw_2d");
    }

    [Fact]
    public async Task InvalidDrawing_SendsNoRequests()
    {
        var client = new RecordingClient();
        var error = await Assert.ThrowsAnyAsync<McpException>(() => Invoke<CreateResult>(client, "Draw2dAsync", ValidJson.Replace("\"radius\":30", "\"radius\":0"), CancellationToken.None));
        Assert.Contains("INVALID_PLAN", error.Message);
        Assert.Empty(client.Methods);
    }

    [Fact]
    public async Task ChangedDrawingWithoutCompletedRequest_DoesNotWrite()
    {
        var client = new RecordingClient { Fingerprint = "changed" };
        var error = await Assert.ThrowsAnyAsync<McpException>(() => Invoke<CreateResult>(client, "Draw2dAsync", ValidJson, CancellationToken.None));
        Assert.Contains("DOCUMENT_CHANGED", error.Message);
        Assert.DoesNotContain("draw_2d", client.Methods);
    }

    [Fact]
    public async Task UndefinedUnit_DoesNotWrite()
    {
        var client = new RecordingClient { Unit = null };
        var error = await Assert.ThrowsAnyAsync<McpException>(() => Invoke<CreateResult>(client, "Draw2dAsync", ValidJson, CancellationToken.None));
        Assert.Contains("UNIT_REQUIRED", error.Message);
        Assert.DoesNotContain("draw_2d", client.Methods);
    }

    private static Task<T> Invoke<T>(RecordingClient client, string methodName, params object?[] args)
    {
        var type = typeof(CadTools).Assembly.GetType("AiCad.Bridge.Cad2dTools");
        Assert.NotNull(type);
        var tool = Activator.CreateInstance(type, client);
        var method = type.GetMethod(methodName);
        Assert.NotNull(method);
        try { return (Task<T>)method.Invoke(tool, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private sealed class RecordingClient : ICadClient
    {
        public List<string> Methods { get; } = new();
        public string Fingerprint { get; init; } = "fingerprint-1";
        public LengthUnit? Unit { get; init; } = LengthUnit.Millimeter;

        public Task<TResponse> CallAsync<TRequest, TResponse>(string method, TRequest request, CancellationToken cancellationToken = default)
        {
            Methods.Add(method);
            object response = method switch
            {
                "get_context" => new CadDocumentContext(Fingerprint, "Drawing1.dwg", Unit, false, Array.Empty<double>(), Array.Empty<CadEntitySummary>()),
                "get_request_status" => new RequestStatusResult(Guid.Parse("55555555-5555-5555-5555-555555555555"), "not_found", null),
                "draw_2d" => new CreateResult(true, "batch", new Dictionary<string, string> { ["bore"] = "A1" }, Array.Empty<string>(), 1),
                _ => throw new InvalidOperationException($"Unexpected RPC {method}")
            };
            return Task.FromResult((TResponse)response);
        }
    }
}
