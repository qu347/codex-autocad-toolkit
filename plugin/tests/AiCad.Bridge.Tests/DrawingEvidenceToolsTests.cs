using AiCad.Contracts;
using ModelContextProtocol.Server;
using Xunit;

namespace AiCad.Bridge.Tests;

public sealed class DrawingEvidenceToolsTests
{
    [Theory]
    [InlineData(nameof(CadTools.GetLayoutsAsync), "cad_get_layouts")]
    [InlineData(nameof(CadTools.GetEntitiesAsync), "cad_get_entities")]
    [InlineData(nameof(CadTools.GetDimensionsAsync), "cad_get_dimensions")]
    [InlineData(nameof(CadTools.GetTextAndTablesAsync), "cad_get_text_and_tables")]
    [InlineData(nameof(CadTools.GetBlocksAsync), "cad_get_blocks")]
    public void EvidenceToolIsDeclaredReadOnly(string methodName, string toolName)
    {
        var method = typeof(CadTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing method {methodName}.");
        var metadata = Assert.Single(method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
            .Cast<McpServerToolAttribute>());

        Assert.Equal(toolName, metadata.Name);
        Assert.True(metadata.ReadOnly);
    }

    [Fact]
    public async Task EntityEvidenceIsPagedWithoutWriteRpc()
    {
        var client = new EvidenceClient();
        var tools = new CadTools(client, new AiCad.Mechanical.RoundedPlatePlanFactory());

        var page = await tools.GetEntitiesAsync("Model", null, 200, TestContext.Current.CancellationToken);

        Assert.Equal(200, page.Items.Count);
        Assert.Equal("200", page.NextCursor);
        Assert.Equal(new[] { "get_entities" }, client.Methods);
    }

    private sealed class EvidenceClient : ICadClient
    {
        public List<string> Methods { get; } = new();

        public Task<TResponse> CallAsync<TRequest, TResponse>(
            string method,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            Methods.Add(method);
            object response = new DrawingEvidencePage<EntityEvidence>(
                Enumerable.Range(1, 200)
                    .Select(index => new EntityEvidence(index.ToString("X"), "Line", "0", null, null))
                    .ToArray(),
                "200",
                501,
                "fingerprint");
            return Task.FromResult((TResponse)response);
        }
    }
}
