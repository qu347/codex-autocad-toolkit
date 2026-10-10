using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(AiCad.AutoCAD.PluginEntry))]

namespace AiCad.AutoCAD;

public sealed class PluginEntry : IExtensionApplication
{
    private const string ProtocolVersion = "1.0";
    private readonly CadRequestQueue _queue = new();
    private readonly AutoCadContextReader _contextReader = new();
    private readonly AutoCadPlanExecutor _planExecutor = new();
    private readonly DrawingEvidenceReader _evidenceReader = new();
    private PipeServerHost? _pipeServer;
    private string? _sessionToken;
    private string? _descriptorPath;

    internal static PluginEntry? Current { get; private set; }

    internal bool IsReady { get; private set; }

    public void Initialize()
    {
        Current = this;
        _sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var pipeName = $"aicad-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var descriptor = new CadSessionDescriptor(
            Environment.ProcessId,
            pipeName,
            _sessionToken,
            typeof(Application).Assembly.GetName().Version?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow);

        _pipeServer = new PipeServerHost(pipeName, _sessionToken, _queue);
        _pipeServer.Start();
        _descriptorPath = WriteDescriptor(descriptor);
        Application.Idle += OnIdle;
        IsReady = true;
    }

    public void Terminate()
    {
        IsReady = false;
        Application.Idle -= OnIdle;
        _pipeServer?.Dispose();
        DeleteOwnDescriptor();
        Current = null;
    }

    internal string GetStatusLine()
    {
        if (!IsReady)
        {
            return "AICAD_NOT_READY";
        }

        var document = Application.DocumentManager.MdiActiveDocument;
        var fingerprint = document is null
            ? "none"
            : _contextReader.ComputeFingerprint(document)[..8];
        return $"AICAD_READY protocol={ProtocolVersion} pipe=available document={fingerprint}";
    }

    private void OnIdle(object? sender, EventArgs eventArgs)
    {
        ProcessPendingRequests();
    }

    internal int ProcessPendingRequests()
    {
        var processed = 0;
        while (processed < 10 && _queue.TryDequeue(out var queued))
        {
            if (queued is null)
            {
                continue;
            }

            try
            {
                queued.Completion.TrySetResult(Dispatch(queued.Request));
            }
            catch (System.Exception exception)
            {
                queued.Completion.TrySetResult(Error(
                    queued.Request.MessageId,
                    "INTERNAL_ERROR",
                    exception.Message));
            }

            processed++;
        }

        return processed;
    }

    private CadRpcResponse Dispatch(CadRpcRequest request)
    {
        return request.Method switch
        {
            "health" => Success(
                request.MessageId,
                new HealthResult(
                    ProtocolVersion,
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                    Environment.ProcessId)),
            "get_context" => ReadContext(request.MessageId),
            "get_layouts" => ReadEvidence(request, _evidenceReader.ReadLayouts),
            "get_entities" => ReadEvidence(request, _evidenceReader.ReadEntities),
            "get_dimensions" => ReadEvidence(request, _evidenceReader.ReadDimensions),
            "get_text_and_tables" => ReadEvidence(request, _evidenceReader.ReadTextAndTables),
            "get_blocks" => ReadEvidence(request, _evidenceReader.ReadBlocks),
            "create_rounded_plate" => CreateRoundedPlate(request),
            "get_request_status" => GetRequestStatus(request),
            "propose_hole_diameter_change" => ProposeHoleDiameterChange(request),
            "apply_change" => ApplyChange(request),
            "cancel_change" => CancelChange(request),
            "undo_batch" => UndoBatch(request),
            _ => Error(request.MessageId, "PROTOCOL_MISMATCH", "The requested CAD method is not supported.")
        };
    }

    private CadRpcResponse ReadEvidence<T>(
        CadRpcRequest request,
        Func<Autodesk.AutoCAD.ApplicationServices.Document, DrawingEvidenceRequest, T> reader)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        DrawingEvidenceRequest? evidenceRequest;
        try
        {
            evidenceRequest = request.Payload.Deserialize<DrawingEvidenceRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_REQUEST", exception.Message);
        }

        if (evidenceRequest is null)
        {
            return Error(request.MessageId, "INVALID_REQUEST", "Drawing evidence request is empty.");
        }

        try
        {
            return Success(request.MessageId, reader(document, evidenceRequest));
        }
        catch (DrawingEvidenceException exception)
        {
            return Error(request.MessageId, exception.Code, exception.Message);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception)
        {
            return Error(request.MessageId, "EVIDENCE_READ_FAILED", exception.Message);
        }
    }

    private CadRpcResponse ReadContext(Guid messageId)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        return document is null
            ? Error(messageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.")
            : Success(messageId, _contextReader.Read(document));
    }

    private CadRpcResponse CreateRoundedPlate(CadRpcRequest request)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        DrawingPlan? plan;
        try
        {
            plan = request.Payload.Deserialize<DrawingPlan>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (plan is null)
        {
            return Error(request.MessageId, "INVALID_PLAN", "The drawing plan payload is empty.");
        }

        try
        {
            return Success(request.MessageId, _planExecutor.ExecuteCreate(document, plan));
        }
        catch (PlanValidationException exception)
        {
            return Error(
                request.MessageId,
                "INVALID_PLAN",
                string.Join("; ", exception.Errors.Select(error => $"{error.Path}: {error.Message}")));
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("read-only", StringComparison.Ordinal))
        {
            return Error(request.MessageId, "READ_ONLY_DOCUMENT", exception.Message);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("changed", StringComparison.Ordinal))
        {
            return Error(request.MessageId, "DOCUMENT_CHANGED", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception)
        {
            return Error(request.MessageId, "GEOMETRY_FAILED", exception.Message);
        }
    }

    private CadRpcResponse GetRequestStatus(CadRpcRequest request)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        RequestStatusRequest? statusRequest;
        try
        {
            statusRequest = request.Payload.Deserialize<RequestStatusRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (statusRequest is null || statusRequest.RequestId == Guid.Empty)
        {
            return Error(request.MessageId, "INVALID_PLAN", "requestId must be a non-empty GUID.");
        }

        return Success(
            request.MessageId,
            _planExecutor.GetRequestStatus(document, statusRequest.RequestId));
    }

    private CadRpcResponse ProposeHoleDiameterChange(CadRpcRequest request)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        ProposeHoleDiameterChangeRequest? proposal;
        try
        {
            proposal = request.Payload.Deserialize<ProposeHoleDiameterChangeRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (proposal is null)
        {
            return Error(request.MessageId, "INVALID_PLAN", "The change proposal payload is empty.");
        }

        try
        {
            return Success(
                request.MessageId,
                _planExecutor.ProposeHoleDiameterChange(
                    document,
                    proposal.BatchId,
                    proposal.NewHoleDiameter));
        }
        catch (CadOperationException exception)
        {
            return Error(request.MessageId, exception.Code, exception.Message);
        }
    }

    private CadRpcResponse ApplyChange(CadRpcRequest request)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        ApplyChangeRequest? change;
        try
        {
            change = request.Payload.Deserialize<ApplyChangeRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (change is null || change.ChangeId == Guid.Empty || string.IsNullOrWhiteSpace(change.ApprovalToken))
        {
            return Error(request.MessageId, "APPROVAL_REQUIRED", "A changeId and approval token are required.");
        }

        try
        {
            return Success(
                request.MessageId,
                _planExecutor.ApplyChange(document, change.ChangeId, change.ApprovalToken));
        }
        catch (CadOperationException exception)
        {
            return Error(request.MessageId, exception.Code, exception.Message);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception)
        {
            return Error(request.MessageId, "GEOMETRY_FAILED", exception.Message);
        }
    }

    private CadRpcResponse CancelChange(CadRpcRequest request)
    {
        CancelChangeRequest? change;
        try
        {
            change = request.Payload.Deserialize<CancelChangeRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (change is null || change.ChangeId == Guid.Empty || string.IsNullOrWhiteSpace(change.ApprovalToken))
        {
            return Error(request.MessageId, "APPROVAL_REQUIRED", "A changeId and approval token are required.");
        }

        try
        {
            return Success(
                request.MessageId,
                _planExecutor.CancelChange(change.ChangeId, change.ApprovalToken));
        }
        catch (CadOperationException exception)
        {
            return Error(request.MessageId, exception.Code, exception.Message);
        }
    }

    private CadRpcResponse UndoBatch(CadRpcRequest request)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return Error(request.MessageId, "INTERNAL_ERROR", "AutoCAD has no active drawing.");
        }

        UndoBatchRequest? undoRequest;
        try
        {
            undoRequest = request.Payload.Deserialize<UndoBatchRequest>(PlanJson.Options);
        }
        catch (JsonException exception)
        {
            return Error(request.MessageId, "INVALID_PLAN", exception.Message);
        }

        if (undoRequest is null || string.IsNullOrWhiteSpace(undoRequest.BatchId))
        {
            return Error(request.MessageId, "INVALID_PLAN", "batchId is required.");
        }

        try
        {
            return Success(
                request.MessageId,
                _planExecutor.UndoBatch(document, undoRequest.BatchId));
        }
        catch (CadOperationException exception)
        {
            return Error(request.MessageId, exception.Code, exception.Message);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception)
        {
            return Error(request.MessageId, "GEOMETRY_FAILED", exception.Message);
        }
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

    private static string WriteDescriptor(CadSessionDescriptor descriptor)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "AiCad", "sessions");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{descriptor.ProcessId}.json");
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(descriptor, PlanJson.Options));
        File.Move(temporaryPath, path, true);
        return path;
    }

    private void DeleteOwnDescriptor()
    {
        if (_descriptorPath is null || _sessionToken is null || !File.Exists(_descriptorPath))
        {
            return;
        }

        try
        {
            var descriptor = JsonSerializer.Deserialize<CadSessionDescriptor>(
                File.ReadAllText(_descriptorPath),
                PlanJson.Options);
            if (descriptor?.SessionToken == _sessionToken)
            {
                File.Delete(_descriptorPath);
            }
        }
        catch (System.Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }
}
