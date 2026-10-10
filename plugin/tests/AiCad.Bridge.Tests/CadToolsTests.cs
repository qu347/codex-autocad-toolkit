using AiCad.Contracts;
using AiCad.Mechanical;
using ModelContextProtocol.Server;
using Xunit;

namespace AiCad.Bridge.Tests;

public sealed class CadToolsTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateRoundedPlate_BuildsOneValidatedCreateRpc()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var result = await tools.CreateRoundedPlateAsync(
            100,
            60,
            10,
            5,
            8,
            10,
            10,
            true,
            true,
            true,
            RequestId.ToString(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var write = Assert.Single(client.Calls, call => call.Method == "create_rounded_plate");
        var plan = Assert.IsType<DrawingPlan>(write.Request);
        Assert.Equal(OperationKind.Create, plan.DeclaredOperation);
        Assert.Equal(LengthUnit.Millimeter, plan.Unit);
        Assert.Equal("fingerprint-1", plan.DocumentFingerprint);
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    [Fact]
    public async Task CreateRoundedPlate_UndefinedUnit_DoesNotSendWriteRpc()
    {
        var client = new RecordingCadClient(Context(null));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var exception = await Assert.ThrowsAsync<CadToolException>(() => tools.CreateRoundedPlateAsync(
            100,
            60,
            10,
            5,
            8,
            10,
            10,
            true,
            true,
            true,
            RequestId.ToString(),
            TestContext.Current.CancellationToken));

        Assert.Equal("UNIT_REQUIRED", exception.Code);
        Assert.DoesNotContain(client.Calls, call => call.Method == "create_rounded_plate");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task CreateRoundedPlate_InvalidWidth_DoesNotSendWriteRpc(double width)
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var exception = await Assert.ThrowsAsync<CadToolException>(() => tools.CreateRoundedPlateAsync(
            width,
            60,
            10,
            5,
            8,
            10,
            10,
            true,
            true,
            true,
            RequestId.ToString(),
            TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_PLAN", exception.Code);
        Assert.DoesNotContain(client.Calls, call => call.Method == "create_rounded_plate");
    }

    [Fact]
    public async Task CreateRoundedPlate_InvalidRequestId_DoesNotSendWriteRpc()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var exception = await Assert.ThrowsAsync<CadToolException>(() => tools.CreateRoundedPlateAsync(
            100,
            60,
            10,
            5,
            8,
            10,
            10,
            true,
            true,
            true,
            "not-a-guid",
            TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_PLAN", exception.Code);
        Assert.DoesNotContain(client.Calls, call => call.Method == "create_rounded_plate");
    }

    [Fact]
    public async Task CreateRoundedPlate_Timeout_DoesNotRetryWrite()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter))
        {
            CreateException = new CadBridgeException("TIMEOUT", "Timed out.")
        };
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var exception = await Assert.ThrowsAsync<CadToolException>(() => tools.CreateRoundedPlateAsync(
            100,
            60,
            10,
            5,
            8,
            10,
            10,
            true,
            true,
            true,
            RequestId.ToString(),
            TestContext.Current.CancellationToken));

        Assert.Equal("TIMEOUT", exception.Code);
        Assert.Contains("cad_get_request_status", exception.Message, StringComparison.Ordinal);
        Assert.Single(client.Calls, call => call.Method == "create_rounded_plate");
    }

    [Fact]
    public async Task ProposeHoleDiameterChange_DoesNotApplyChange()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var proposal = await tools.ProposeHoleDiameterChangeAsync(
            "batch-1",
            10,
            TestContext.Current.CancellationToken);

        Assert.Equal("Change four holes from diameter 8 to diameter 10.", proposal.Summary);
        Assert.Single(client.Calls, call => call.Method == "propose_hole_diameter_change");
        Assert.DoesNotContain(client.Calls, call => call.Method == "apply_change");
    }

    [Fact]
    public async Task ApplyChange_WithoutToken_DoesNotSendRpc()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var exception = await Assert.ThrowsAsync<CadToolException>(() => tools.ApplyChangeAsync(
            Guid.NewGuid().ToString(),
            string.Empty,
            TestContext.Current.CancellationToken));

        Assert.Equal("APPROVAL_REQUIRED", exception.Code);
        Assert.DoesNotContain(client.Calls, call => call.Method == "apply_change");
    }

    [Fact]
    public async Task CancelChange_SendsOnlyCancelRpc()
    {
        var client = new RecordingCadClient(Context(LengthUnit.Millimeter));
        var tools = new CadTools(client, new RoundedPlatePlanFactory());

        var result = await tools.CancelChangeAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222").ToString(),
            "approval-token",
            TestContext.Current.CancellationToken);

        Assert.True(result.Cancelled);
        Assert.Single(client.Calls, call => call.Method == "cancel_change");
        Assert.DoesNotContain(client.Calls, call => call.Method == "apply_change");
    }

    [Theory]
    [InlineData(nameof(CadTools.HealthAsync), "cad_health", true)]
    [InlineData(nameof(CadTools.GetContextAsync), "cad_get_context", true)]
    [InlineData(nameof(CadTools.GetRequestStatusAsync), "cad_get_request_status", true)]
    [InlineData(nameof(CadTools.CreateRoundedPlateAsync), "cad_create_rounded_plate", false)]
    [InlineData(nameof(CadTools.ProposeHoleDiameterChangeAsync), "cad_propose_hole_diameter_change", true)]
    [InlineData(nameof(CadTools.ApplyChangeAsync), "cad_apply_change", false)]
    [InlineData(nameof(CadTools.CancelChangeAsync), "cad_cancel_change", false)]
    [InlineData(nameof(CadTools.UndoBatchAsync), "cad_undo_batch", false)]
    public void ToolMetadata_DeclaresNameAndReadOnlyBehavior(string methodName, string toolName, bool readOnly)
    {
        var method = typeof(CadTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing method {methodName}.");
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
            .Cast<McpServerToolAttribute>());

        Assert.Equal(toolName, attribute.Name);
        Assert.Equal(readOnly, attribute.ReadOnly);
    }

    private static CadDocumentContext Context(LengthUnit? unit)
    {
        return new CadDocumentContext(
            "fingerprint-1",
            "Drawing1.dwg",
            unit,
            false,
            new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 },
            Array.Empty<CadEntitySummary>());
    }

    private sealed class RecordingCadClient : ICadClient
    {
        private readonly CadDocumentContext _context;

        public RecordingCadClient(CadDocumentContext context)
        {
            _context = context;
        }

        public List<(string Method, object? Request)> Calls { get; } = new();

        public Exception? CreateException { get; init; }

        public Task<TResponse> CallAsync<TRequest, TResponse>(
            string method,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((method, request));
            object response = method switch
            {
                "health" => new HealthResult("1.0", "1.0.0", 42),
                "get_context" => _context,
                "create_rounded_plate" when CreateException is not null => throw CreateException,
                "create_rounded_plate" => new CreateResult(
                    true,
                    "batch-1",
                    new Dictionary<string, string> { ["plate-solid"] = "2A" },
                    Array.Empty<string>(),
                    5),
                "get_request_status" => new RequestStatusResult(
                    RequestId,
                    "completed",
                    new CreateResult(true, "batch-1", new Dictionary<string, string>(), Array.Empty<string>(), 0)),
                "propose_hole_diameter_change" => new ProposedChangeResult(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "approval-token",
                    "Change four holes from diameter 8 to diameter 10.",
                    DateTimeOffset.UtcNow.AddMinutes(5)),
                "apply_change" => new CreateResult(
                    true,
                    "batch-2",
                    new Dictionary<string, string>(),
                    Array.Empty<string>(),
                    8),
                "cancel_change" => new CancelChangeResult(true),
                "undo_batch" => new UndoBatchResult(true, "batch-undo", "batch-2", null),
                _ => throw new InvalidOperationException($"Unexpected RPC method {method}.")
            };
            return Task.FromResult((TResponse)response);
        }
    }
}
