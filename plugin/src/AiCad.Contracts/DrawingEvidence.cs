namespace AiCad.Contracts;

public sealed record DrawingEvidenceRequest(
    string? Layout,
    string? Cursor,
    int PageSize = 200);

public sealed record DrawingEvidencePage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int TotalCount,
    string DocumentFingerprint);

public sealed record LayoutEvidence(
    string Name,
    bool IsModelSpace,
    int EntityCount);

public sealed record EntityEvidence(
    string Handle,
    string EntityType,
    string Layer,
    Point3? Min,
    Point3? Max);

public sealed record DimensionEvidence(
    string Handle,
    string DimensionType,
    string Layer,
    double Measurement,
    string DisplayText,
    Point3? TextPosition);

public sealed record TextTableEvidence(
    string Handle,
    string EvidenceType,
    string Layer,
    string Text,
    int? Rows,
    int? Columns,
    Point3? Position);

public sealed record BlockEvidence(
    string Handle,
    string BlockName,
    string Layer,
    Point3 Position,
    double RotationRadians,
    IReadOnlyDictionary<string, string> Attributes);
