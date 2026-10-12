namespace AiCad.Contracts;

public static class Drawing2dValidator
{
    public static PlanValidationResult Validate(Drawing2dPlan plan)
    {
        var errors = new List<PlanError>();
        void Error(string path, string message) => errors.Add(new PlanError("INVALID_PLAN", path, message));
        if (plan.SchemaVersion != "2d-1.0") Error("schemaVersion", "Use 2d-1.0.");
        if (plan.RequestId == Guid.Empty) Error("requestId", "A nonempty GUID is required.");
        if (string.IsNullOrWhiteSpace(plan.DocumentFingerprint)) Error("documentFingerprint", "Read cad_get_context first.");
        if (!Enum.IsDefined(plan.Unit)) Error("unit", "Unsupported length unit.");
        if (plan.Layers is null || plan.Layers.Count > 32) Error("layers", "Supply an array of at most 32 layers.");
        if (plan.Entities is null || plan.Entities.Count > 500) Error("entities", "Supply an array of at most 500 entities.");
        if (plan.Layers is null || plan.Entities is null) return new(errors);
        if (plan.Layers.Count + plan.Entities.Count == 0) Error("entities", "At least one layer or entity is required.");
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in plan.Layers)
        {
            if (layer is null || !IsName(layer.Name) || !layers.Add(layer.Name))
            { Error("layers", "Layer names must be valid and unique."); continue; }
            if (layer.Color is < 1 or > 255) Error("layers", "Layer color must be an ACI from 1 through 255.");
            if (layer.LineType is not ("Continuous" or "CENTER" or "HIDDEN")) Error("layers", "Supported line types: Continuous, CENTER, HIDDEN.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < plan.Entities.Count; index++)
        {
            var entity = plan.Entities[index];
            var path = $"entities[{index}]";
            if (entity is null || !IsName(entity.Id) || !ids.Add(entity.Id) || !IsName(entity.Layer))
            { Error(path, "Every entity requires a unique ID and valid layer name."); continue; }
            var valid = entity switch
            {
                Line2d line => Distinct(line.Start, line.End),
                Circle2d circle => IsPoint(circle.Center) && Positive(circle.Radius),
                Arc2d arc => IsPoint(arc.Center) && Positive(arc.Radius)
                    && Angle(arc.StartAngleDegrees) && Angle(arc.EndAngleDegrees)
                    && Math.Abs(arc.StartAngleDegrees - arc.EndAngleDegrees) > 1e-9,
                Polyline2d polyline => ValidPolyline(polyline),
                AlignedDimension2d dim => Distinct(dim.First, dim.Second) && IsPoint(dim.LinePoint)
                    && Math.Abs((dim.Second.X - dim.First.X) * (dim.LinePoint.Y - dim.First.Y)
                        - (dim.Second.Y - dim.First.Y) * (dim.LinePoint.X - dim.First.X)) > 1e-9 && Prefix(dim.Prefix),
                LinearDimension2d dim => Distinct(dim.First, dim.Second) && IsPoint(dim.LinePoint)
                    && dim.RotationDegrees is 0 or 90 && Prefix(dim.Prefix)
                    && Math.Abs(dim.RotationDegrees == 0 ? dim.Second.X - dim.First.X : dim.Second.Y - dim.First.Y) > 1e-9,
                RadialDimension2d dim => Distinct(dim.Center, dim.Chord) && Positive(dim.LeaderLength) && Prefix(dim.Prefix)
                    && (dim.TextPosition is null || IsPoint(dim.TextPosition)),
                DiameterDimension2d dim => Distinct(dim.Chord, dim.FarChord) && Positive(dim.LeaderLength) && Prefix(dim.Prefix)
                    && (dim.TextPosition is null || IsPoint(dim.TextPosition)),
                _ => false
            };
            if (!valid) Error(path, "Invalid or degenerate geometry, angle, dimension, or prefix.");
        }
        return new(errors);
    }

    public static void RequireContext(Drawing2dPlan plan, CadDocumentContext context)
    {
        if (context.IsReadOnly) throw new Drawing2dException("READ_ONLY_DOCUMENT", "The active drawing is read-only.");
        if (context.Unit is null) throw new Drawing2dException("UNIT_REQUIRED", "Define the drawing unit before drawing.");
        if (context.Unit != plan.Unit) throw new Drawing2dException("UNIT_MISMATCH", "Plan and active drawing units differ.");
        if (context.DocumentFingerprint != plan.DocumentFingerprint)
            throw new Drawing2dException("DOCUMENT_CHANGED", "Refresh cad_get_context; the active drawing changed.");
    }

    public static bool IsName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
        && !value.Any(c => char.IsControl(c) || "<>/\\\":;?*|,=".Contains(c));
    private static bool Number(double value) => double.IsFinite(value) && Math.Abs(value) <= 1_000_000_000;
    private static bool Positive(double value) => Number(value) && value > 0;
    private static bool Angle(double value) => double.IsFinite(value) && value >= 0 && value < 360;
    private static bool IsPoint(Point2? point) => point is not null && Number(point.X) && Number(point.Y);
    private static bool Distinct(Point2? first, Point2? second) => IsPoint(first) && IsPoint(second)
        && Distance(first!.X - second!.X, first.Y - second.Y) > 1e-9;
    private static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);
    private static bool Prefix(string? value) => value is not null && value.Length <= 32
        && !value.Any(c => char.IsControl(c) || "\\<>%".Contains(c));
    private static bool ValidPolyline(Polyline2d line)
    {
        if (line.Vertices is null || line.Vertices.Count < (line.Closed ? 3 : 2) || line.Vertices.Count > 256) return false;
        for (var i = 0; i < line.Vertices.Count; i++)
        {
            var vertex = line.Vertices[i];
            if (vertex is null || !Number(vertex.X) || !Number(vertex.Y) || !Number(vertex.Bulge)) return false;
            if (i > 0 && Distance(vertex.X - line.Vertices[i - 1].X, vertex.Y - line.Vertices[i - 1].Y) <= 1e-9) return false;
        }
        if (!line.Closed && line.Vertices[^1].Bulge != 0) return false;
        return !line.Closed || Distance(line.Vertices[0].X - line.Vertices[^1].X, line.Vertices[0].Y - line.Vertices[^1].Y) > 1e-9;
    }
}
