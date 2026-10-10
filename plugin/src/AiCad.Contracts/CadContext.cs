namespace AiCad.Contracts;

public sealed record HealthResult(
    string ProtocolVersion,
    string PluginVersion,
    int AutoCadProcessId);

public sealed record CreateResult(
    bool Success,
    string BatchId,
    IReadOnlyDictionary<string, string> Handles,
    IReadOnlyList<string> Warnings,
    long ElapsedMilliseconds);

public sealed record RequestStatusRequest(Guid RequestId);

public sealed record RequestStatusResult(
    Guid RequestId,
    string Status,
    CreateResult? Result);

public sealed record ProposeHoleDiameterChangeRequest(
    string BatchId,
    double NewHoleDiameter);

public sealed record ProposedChangeResult(
    Guid ChangeId,
    string ApprovalToken,
    string Summary,
    DateTimeOffset ExpiresAt);

public sealed record ApplyChangeRequest(Guid ChangeId, string ApprovalToken);

public sealed record CancelChangeRequest(Guid ChangeId, string ApprovalToken);

public sealed record CancelChangeResult(bool Cancelled);

public sealed record UndoBatchRequest(string BatchId);

public sealed record UndoBatchResult(
    bool Success,
    string UndoBatchId,
    string UndoneBatchId,
    CreateResult? RestoredResult);

public sealed record CadDocumentContext(
    string DocumentFingerprint,
    string DrawingName,
    LengthUnit? Unit,
    bool IsReadOnly,
    IReadOnlyList<double> CurrentUcs,
    IReadOnlyList<CadEntitySummary> Selection);

public sealed record CadEntitySummary(
    string Handle,
    string Type,
    Point3 Min,
    Point3 Max);
