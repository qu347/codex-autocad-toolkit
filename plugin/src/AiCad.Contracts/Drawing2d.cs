using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiCad.Contracts;

public sealed record Point2([property: JsonRequired] double X, [property: JsonRequired] double Y);
public sealed record PolylineVertex2([property: JsonRequired] double X, [property: JsonRequired] double Y, double Bulge = 0);
public sealed record Layer2d(string Name, short Color = 7, string LineType = "Continuous");

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Line2d), "line")]
[JsonDerivedType(typeof(Circle2d), "circle")]
[JsonDerivedType(typeof(Arc2d), "arc")]
[JsonDerivedType(typeof(Polyline2d), "polyline")]
[JsonDerivedType(typeof(AlignedDimension2d), "aligned_dimension")]
[JsonDerivedType(typeof(LinearDimension2d), "linear_dimension")]
[JsonDerivedType(typeof(RadialDimension2d), "radial_dimension")]
[JsonDerivedType(typeof(DiameterDimension2d), "diameter_dimension")]
public abstract record Entity2d(string Id, string Layer);

public sealed record Line2d(string Id, string Layer, [property: JsonRequired] Point2 Start, [property: JsonRequired] Point2 End) : Entity2d(Id, Layer);
public sealed record Circle2d(string Id, string Layer, [property: JsonRequired] Point2 Center, [property: JsonRequired] double Radius) : Entity2d(Id, Layer);
public sealed record Arc2d(string Id, string Layer, [property: JsonRequired] Point2 Center, [property: JsonRequired] double Radius,
    [property: JsonRequired] double StartAngleDegrees, [property: JsonRequired] double EndAngleDegrees) : Entity2d(Id, Layer);
public sealed record Polyline2d(string Id, string Layer, [property: JsonRequired] IReadOnlyList<PolylineVertex2> Vertices,
    [property: JsonRequired] bool Closed) : Entity2d(Id, Layer);
public sealed record AlignedDimension2d(string Id, string Layer, [property: JsonRequired] Point2 First,
    [property: JsonRequired] Point2 Second, [property: JsonRequired] Point2 LinePoint, string Prefix = "") : Entity2d(Id, Layer);
public sealed record LinearDimension2d(string Id, string Layer, [property: JsonRequired] Point2 First,
    [property: JsonRequired] Point2 Second, [property: JsonRequired] Point2 LinePoint,
    double RotationDegrees = 0, string Prefix = "") : Entity2d(Id, Layer);
public sealed record RadialDimension2d(string Id, string Layer, [property: JsonRequired] Point2 Center,
    [property: JsonRequired] Point2 Chord, double LeaderLength = 10, string Prefix = "") : Entity2d(Id, Layer);
public sealed record DiameterDimension2d(string Id, string Layer, [property: JsonRequired] Point2 Chord,
    [property: JsonRequired] Point2 FarChord, double LeaderLength = 10, string Prefix = "") : Entity2d(Id, Layer);

public sealed record Drawing2dPlan(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string DocumentFingerprint,
    [property: JsonRequired] LengthUnit Unit,
    [property: JsonRequired] IReadOnlyList<Layer2d> Layers,
    [property: JsonRequired] IReadOnlyList<Entity2d> Entities)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static Drawing2dPlan Parse(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1_048_576)
                throw new JsonException("A bounded nonempty planJson is required.");
            var plan = JsonSerializer.Deserialize<Drawing2dPlan>(json, JsonOptions)
                ?? throw new JsonException("The plan must be a JSON object.");
            var validation = Drawing2dValidator.Validate(plan);
            if (!validation.IsValid) throw new PlanValidationException(validation.Errors);
            return plan;
        }
        catch (JsonException error)
        {
            throw new PlanValidationException(new[] { new PlanError("INVALID_JSON", error.Path ?? "planJson", error.Message) });
        }
        catch (NotSupportedException error)
        {
            throw new PlanValidationException(new[] { new PlanError("UNSUPPORTED_ENTITY", "entities", error.Message) });
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed class Drawing2dException : Exception
{
    public Drawing2dException(string code, string message) : base(message) { Code = code; }
    public string Code { get; }
}

public sealed record GeometryReadRequest(IReadOnlyList<string> Handles);
public sealed record GeometryEvidence(string Handle, string EntityType, string Layer, JsonElement Data);
public sealed record GeometryReadResult(IReadOnlyList<GeometryEvidence> Items, string DocumentFingerprint);
public sealed record LayerEvidence(string Name, short Color, string LineType, bool IsLocked, bool IsOff, bool IsFrozen);
public sealed record SaveDrawingRequest(string DocumentFingerprint, string? DwgPath, string? DxfPath);
public sealed record SavedDrawingFile(string Path, long Bytes);
public sealed record SaveDrawingResult(bool Success, IReadOnlyList<SavedDrawingFile> Files, string DocumentFingerprint);
