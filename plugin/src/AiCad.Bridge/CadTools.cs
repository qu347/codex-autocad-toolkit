using System.ComponentModel;
using AiCad.Contracts;
using AiCad.Mechanical;
using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace AiCad.Bridge;

public sealed class CadToolException : McpException
{
    public CadToolException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

[McpServerToolType]
public sealed class CadTools
{
    private readonly ICadClient _cadClient;
    private readonly RoundedPlatePlanFactory _factory;

    public CadTools(ICadClient cadClient, RoundedPlatePlanFactory factory)
    {
        _cadClient = cadClient;
        _factory = factory;
    }

    [McpServerTool(Name = "cad_health", ReadOnly = true, OpenWorld = false)]
    [Description("Check whether the local AutoCAD AI-CAD plugin is connected and return protocol, plugin, and process information.")]
    public Task<HealthResult> HealthAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync<EmptyRequest, HealthResult>("health", new EmptyRequest(), cancellationToken);
    }

    [McpServerTool(Name = "cad_get_context", ReadOnly = true, OpenWorld = false)]
    [Description("Read the active AutoCAD drawing fingerprint, drawing unit, UCS, read-only state, and selected entity summaries. The drawing unit must be defined before creating geometry.")]
    public Task<CadDocumentContext> GetContextAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync<EmptyRequest, CadDocumentContext>("get_context", new EmptyRequest(), cancellationToken);
    }

    [McpServerTool(Name = "cad_get_layouts", ReadOnly = true, OpenWorld = false)]
    [Description("Read a bounded page of AutoCAD layouts without modifying the DWG. Use nextCursor to continue.")]
    public Task<DrawingEvidencePage<LayoutEvidence>> GetLayoutsAsync(
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(cursor, pageSize);
        return CallAsync<DrawingEvidenceRequest, DrawingEvidencePage<LayoutEvidence>>(
            "get_layouts",
            new DrawingEvidenceRequest(null, cursor, pageSize),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_get_entities", ReadOnly = true, OpenWorld = false)]
    [Description("Read a bounded page of entity type, handle, layer, and extents evidence from one layout without modifying the DWG.")]
    public Task<DrawingEvidencePage<EntityEvidence>> GetEntitiesAsync(
        string layout,
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateLayoutAndPage(layout, cursor, pageSize);
        return CallAsync<DrawingEvidenceRequest, DrawingEvidencePage<EntityEvidence>>(
            "get_entities",
            new DrawingEvidenceRequest(layout, cursor, pageSize),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_get_dimensions", ReadOnly = true, OpenWorld = false)]
    [Description("Read a bounded page of dimension measurements and displayed text from one layout without modifying the DWG.")]
    public Task<DrawingEvidencePage<DimensionEvidence>> GetDimensionsAsync(
        string layout,
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateLayoutAndPage(layout, cursor, pageSize);
        return CallAsync<DrawingEvidenceRequest, DrawingEvidencePage<DimensionEvidence>>(
            "get_dimensions",
            new DrawingEvidenceRequest(layout, cursor, pageSize),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_get_text_and_tables", ReadOnly = true, OpenWorld = false)]
    [Description("Read a bounded page of DBText, MText, and table evidence from one layout without modifying the DWG.")]
    public Task<DrawingEvidencePage<TextTableEvidence>> GetTextAndTablesAsync(
        string layout,
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateLayoutAndPage(layout, cursor, pageSize);
        return CallAsync<DrawingEvidenceRequest, DrawingEvidencePage<TextTableEvidence>>(
            "get_text_and_tables",
            new DrawingEvidenceRequest(layout, cursor, pageSize),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_get_blocks", ReadOnly = true, OpenWorld = false)]
    [Description("Read a bounded page of block insert names, transforms, and attributes from one layout without modifying the DWG.")]
    public Task<DrawingEvidencePage<BlockEvidence>> GetBlocksAsync(
        string layout,
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        ValidateLayoutAndPage(layout, cursor, pageSize);
        return CallAsync<DrawingEvidenceRequest, DrawingEvidencePage<BlockEvidence>>(
            "get_blocks",
            new DrawingEvidenceRequest(layout, cursor, pageSize),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_get_request_status", ReadOnly = true, OpenWorld = false)]
    [Description("Check a prior CAD create request by requestId. Use this after a timeout; do not retry creation with a new requestId until status is known.")]
    public Task<RequestStatusResult> GetRequestStatusAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(requestId, out var parsedRequestId))
        {
            throw new CadToolException("INVALID_PLAN", "requestId must be a GUID.");
        }

        return CallAsync<RequestStatusRequest, RequestStatusResult>(
            "get_request_status",
            new RequestStatusRequest(parsedRequestId),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_create_rounded_plate", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Create a rounded rectangular mechanical plate with four through holes, optional 3D solid, 2D three-view drawing, and dimensions. Creation executes immediately. Supply width, depth, thickness, cornerRadius, holeDiameter, edgeOffsetX, and edgeOffsetY in the active drawing unit. A reference image alone cannot establish absolute size: obtain every required dimension first. Reuse one requestId GUID for one user intent; after timeout call cad_get_request_status instead of resending.")]
    public async Task<CreateResult> CreateRoundedPlateAsync(
        double width,
        double depth,
        double thickness,
        double cornerRadius,
        double holeDiameter,
        double edgeOffsetX,
        double edgeOffsetY,
        bool create2d,
        bool create3d,
        bool addDimensions,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(requestId, out var parsedRequestId))
        {
            throw new CadToolException("INVALID_PLAN", "requestId must be a GUID.");
        }

        var context = await CallAsync<EmptyRequest, CadDocumentContext>(
            "get_context",
            new EmptyRequest(),
            cancellationToken).ConfigureAwait(false);
        if (context.Unit is null)
        {
            throw new CadToolException(
                "UNIT_REQUIRED",
                "The active drawing unit is undefined. Ask the user whether the dimensions are millimeters or inches before creating geometry.");
        }

        if (context.IsReadOnly)
        {
            throw new CadToolException("READ_ONLY_DOCUMENT", "The active AutoCAD drawing is read-only.");
        }

        DrawingPlan plan;
        try
        {
            plan = _factory.Create(
                new RoundedPlateRequest(
                    width,
                    depth,
                    thickness,
                    cornerRadius,
                    holeDiameter,
                    edgeOffsetX,
                    edgeOffsetY,
                    create2d,
                    create3d,
                    addDimensions),
                context.DocumentFingerprint,
                parsedRequestId,
                context.Unit.Value);
        }
        catch (PlanValidationException exception)
        {
            var details = string.Join("; ", exception.Errors.Select(error => $"{error.Path}: {error.Message}"));
            throw new CadToolException("INVALID_PLAN", details, exception);
        }

        try
        {
            return await _cadClient.CallAsync<DrawingPlan, CreateResult>(
                "create_rounded_plate",
                plan,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CadBridgeException exception) when (exception.Code == "TIMEOUT")
        {
            throw new CadToolException(
                "TIMEOUT",
                $"AutoCAD did not answer in time. Call cad_get_request_status with requestId {parsedRequestId} before deciding whether to retry.",
                exception);
        }
        catch (CadBridgeException exception)
        {
            throw new CadToolException(exception.Code, exception.Message, exception);
        }
    }

    [McpServerTool(Name = "cad_propose_hole_diameter_change", ReadOnly = true, OpenWorld = false)]
    [Description("Prepare a change that replaces the four mounting-hole diameters in one AI-CAD batch. This reads the drawing and returns a five-minute approval token and summary; it does not modify or delete CAD entities. Show the summary to the user and call cad_apply_change only after explicit confirmation.")]
    public Task<ProposedChangeResult> ProposeHoleDiameterChangeAsync(
        string batchId,
        double newHoleDiameter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId) || !double.IsFinite(newHoleDiameter) || newHoleDiameter <= 0)
        {
            throw new CadToolException("INVALID_PLAN", "batchId and a positive finite newHoleDiameter are required.");
        }

        return CallAsync<ProposeHoleDiameterChangeRequest, ProposedChangeResult>(
            "propose_hole_diameter_change",
            new ProposeHoleDiameterChangeRequest(batchId, newHoleDiameter),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_apply_change", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Apply a previously proposed CAD modification. Call only after the user explicitly confirms the exact proposal summary. Requires the unexpired one-time changeId and approvalToken returned by cad_propose_hole_diameter_change.")]
    public Task<CreateResult> ApplyChangeAsync(
        string changeId,
        string approvalToken,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(changeId, out var parsedChangeId))
        {
            throw new CadToolException("INVALID_PLAN", "changeId must be a GUID.");
        }

        if (string.IsNullOrWhiteSpace(approvalToken))
        {
            throw new CadToolException("APPROVAL_REQUIRED", "An approval token is required before modifying the drawing.");
        }

        return CallAsync<ApplyChangeRequest, CreateResult>(
            "apply_change",
            new ApplyChangeRequest(parsedChangeId, approvalToken),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_cancel_change", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cancel a pending CAD modification by invalidating its approval token. This does not write to the DWG and is safe to call when the user says cancel.")]
    public Task<CancelChangeResult> CancelChangeAsync(
        string changeId,
        string approvalToken,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(changeId, out var parsedChangeId) || string.IsNullOrWhiteSpace(approvalToken))
        {
            throw new CadToolException("APPROVAL_REQUIRED", "A valid changeId and approval token are required to cancel the proposal.");
        }

        return CallAsync<CancelChangeRequest, CancelChangeResult>(
            "cancel_change",
            new CancelChangeRequest(parsedChangeId, approvalToken),
            cancellationToken);
    }

    [McpServerTool(Name = "cad_undo_batch", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Undo the most recent AI-CAD batch using a checked compensating transaction. This may delete or rebuild AI-created entities. Call only after the user explicitly confirms the batchId to undo.")]
    public Task<UndoBatchResult> UndoBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId))
        {
            throw new CadToolException("INVALID_PLAN", "batchId is required.");
        }

        return CallAsync<UndoBatchRequest, UndoBatchResult>(
            "undo_batch",
            new UndoBatchRequest(batchId),
            cancellationToken);
    }

    private async Task<TResponse> CallAsync<TRequest, TResponse>(
        string method,
        TRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cadClient.CallAsync<TRequest, TResponse>(
                method,
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (CadBridgeException exception)
        {
            throw new CadToolException(exception.Code, exception.Message, exception);
        }
    }

    private static void ValidateLayoutAndPage(string layout, string? cursor, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(layout))
        {
            throw new CadToolException("INVALID_LAYOUT", "layout is required.");
        }

        ValidatePage(cursor, pageSize);
    }

    private static void ValidatePage(string? cursor, int pageSize)
    {
        if (pageSize < 1 || pageSize > 500)
        {
            throw new CadToolException("INVALID_PAGE_SIZE", "pageSize must be from 1 through 500.");
        }

        if (cursor is not null && (!int.TryParse(cursor, out var offset) || offset < 0))
        {
            throw new CadToolException("INVALID_CURSOR", "cursor is invalid.");
        }
    }
}
