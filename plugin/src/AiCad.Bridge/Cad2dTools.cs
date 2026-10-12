using System.ComponentModel;
using System.Text.Json;
using AiCad.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AiCad.Bridge;

[McpServerToolType]
public sealed class Cad2dTools
{
    private readonly ICadClient _client;
    public Cad2dTools(ICadClient client) { _client = client; }

    [McpServerTool(Name = "cad_draw_2d", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Create one 2D drawing stage from planJson using schemaVersion 2d-1.0, requestId GUID, documentFingerprint from cad_get_context, explicit unit, layers, and entities. Types: line, circle, arc, polyline (bulge vertices), aligned_dimension, linear_dimension (0/90 degrees), radial_dimension, diameter_dimension. Uses WCS, Z=0; arc angles are degrees, CCW. This tool executes immediately and accepts no code or CAD commands. Reuse the identical plan and requestId after timeout; query cad_get_request_status before retrying. Verify returned handles with cad_get_geometry.")]
    public async Task<CreateResult> Draw2dAsync(string planJson, CancellationToken cancellationToken = default)
    {
        Drawing2dPlan plan;
        try { plan = Drawing2dPlan.Parse(planJson); }
        catch (PlanValidationException error) { throw InvalidPlan(error); }
        var context = await Call<EmptyRequest, CadDocumentContext>("get_context", new(), cancellationToken);
        if (context.DocumentFingerprint != plan.DocumentFingerprint)
        {
            var status = await Call<RequestStatusRequest, RequestStatusResult>("get_request_status", new(plan.RequestId), cancellationToken);
            if (status.Status == "completed")
                context = context with { DocumentFingerprint = plan.DocumentFingerprint };
        }
        try { Drawing2dValidator.RequireContext(plan, context); }
        catch (Drawing2dException error) { throw new McpException($"{error.Code}: {error.Message}"); }
        return await Call<Drawing2dPlan, CreateResult>("draw_2d", plan, cancellationToken);
    }

    [McpServerTool(Name = "cad_get_layers", ReadOnly = true, OpenWorld = false)]
    [Description("Read a page of actual AutoCAD layer names, ACI colors, line types and locked/off/frozen states. Continue using nextCursor. No DWG changes.")]
    public Task<DrawingEvidencePage<LayerEvidence>> GetLayersAsync(string? cursor = null, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 500 || (cursor is not null && (!int.TryParse(cursor, out var offset) || offset < 0)))
            throw new McpException("INVALID_PAGE: pageSize must be 1-500 and cursor a nonnegative integer.");
        return Call<DrawingEvidenceRequest, DrawingEvidencePage<LayerEvidence>>("get_layers", new(null, cursor, pageSize), cancellationToken);
    }

    [McpServerTool(Name = "cad_get_geometry", ReadOnly = true, OpenWorld = false)]
    [Description("Inspect up to 20 actual 2D AutoCAD entity handles, supplied as a JSON string array handlesJson. Returns endpoints, centers/radii, arc angles, polyline vertices/bulges and closure, or native dimension measurements. Use handles returned by cad_draw_2d or cad_get_entities. Read-only.")]
    public Task<GeometryReadResult> GetGeometryAsync(string handlesJson, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>? handles;
        try { handles = JsonSerializer.Deserialize<string[]>(handlesJson); }
        catch (JsonException) { throw new McpException("INVALID_HANDLES: handlesJson must be a JSON string array."); }
        if (handles is null || handles.Count is < 1 or > 20
            || handles.Any(h => h is null || h.Length is < 1 or > 16 || !h.All(Uri.IsHexDigit))
            || handles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != handles.Count)
            throw new McpException("INVALID_HANDLES: supply 1-20 unique hexadecimal handles.");
        return Call<GeometryReadRequest, GeometryReadResult>("get_geometry", new(handles), cancellationToken);
    }

    [McpServerTool(Name = "cad_save_drawing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Save copies of the active drawing to new absolute dwgPath and/or dxfPath. Supply its current documentFingerprint from cad_get_context. At least one path is required; parent directories must exist. Existing files and the source DWG are never overwritten. Return paths and actual byte sizes. After a timeout inspect output files before deciding whether to retry.")]
    public async Task<SaveDrawingResult> SaveDrawingAsync(string documentFingerprint, string? dwgPath = null, string? dxfPath = null, CancellationToken cancellationToken = default)
    {
        try { DrawingFilePolicy.Validate(dwgPath, dxfPath, null); }
        catch (Drawing2dException error) { throw new McpException($"{error.Code}: {error.Message}"); }
        var context = await Call<EmptyRequest, CadDocumentContext>("get_context", new(), cancellationToken);
        if (context.DocumentFingerprint != documentFingerprint) throw new McpException("DOCUMENT_CHANGED: refresh cad_get_context before saving.");
        if (context.IsReadOnly) throw new McpException("READ_ONLY_DOCUMENT: the drawing is read-only.");
        return await Call<SaveDrawingRequest, SaveDrawingResult>("save_drawing", new(documentFingerprint, dwgPath, dxfPath), cancellationToken);
    }

    private async Task<TResponse> Call<TRequest, TResponse>(string method, TRequest request, CancellationToken token)
    {
        try { return await _client.CallAsync<TRequest, TResponse>(method, request, token); }
        catch (CadBridgeException error) { throw new McpException($"{error.Code}: {error.Message}"); }
    }

    private static McpException InvalidPlan(PlanValidationException error) =>
        new("INVALID_PLAN: " + string.Join("; ", error.Errors.Select(e => $"{e.Path}: {e.Message}")));
}
