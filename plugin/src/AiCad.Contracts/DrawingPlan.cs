using System.Text.Json.Serialization;

namespace AiCad.Contracts;

public enum OperationKind
{
    Create,
    Modify,
    Delete,
    Mixed
}

public enum LengthUnit
{
    Millimeter,
    Inch,
    DrawingUnit
}

public sealed record Point3(double X, double Y, double Z);

public sealed record DrawingOutput(bool Create3D, bool Create2D, bool AddDimensions);

public sealed record PlanAssumption(string Name, string Value, bool Estimated);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RoundedPlateStep), "rounded_plate")]
[JsonDerivedType(typeof(FourHolePatternStep), "four_hole_pattern")]
public abstract record FeatureStep(string SemanticId, OperationKind Operation);

public sealed record RoundedPlateStep(
    string SemanticId,
    OperationKind Operation,
    double Width,
    double Depth,
    double Thickness,
    double CornerRadius)
    : FeatureStep(SemanticId, Operation);

public sealed record FourHolePatternStep(
    string SemanticId,
    OperationKind Operation,
    string TargetSemanticId,
    double Diameter,
    double EdgeOffsetX,
    double EdgeOffsetY)
    : FeatureStep(SemanticId, Operation);

public sealed record DrawingPlan(
    string SchemaVersion,
    Guid RequestId,
    string DocumentFingerprint,
    LengthUnit Unit,
    Point3 Origin,
    OperationKind DeclaredOperation,
    IReadOnlyList<FeatureStep> Steps,
    DrawingOutput Output,
    IReadOnlyList<PlanAssumption> Assumptions);
