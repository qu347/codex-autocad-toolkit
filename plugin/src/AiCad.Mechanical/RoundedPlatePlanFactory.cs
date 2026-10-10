using AiCad.Contracts;

namespace AiCad.Mechanical;

public sealed record RoundedPlateRequest(
    double Width,
    double Depth,
    double Thickness,
    double CornerRadius,
    double HoleDiameter,
    double EdgeOffsetX,
    double EdgeOffsetY,
    bool Create2D = true,
    bool Create3D = true,
    bool AddDimensions = true);

public sealed class RoundedPlatePlanFactory
{
    public DrawingPlan Create(
        RoundedPlateRequest request,
        string documentFingerprint,
        Guid requestId,
        LengthUnit unit,
        Point3? origin = null)
    {
        var plan = new DrawingPlan(
            "1.0",
            requestId,
            documentFingerprint,
            unit,
            origin ?? new Point3(0, 0, 0),
            OperationKind.Create,
            new FeatureStep[]
            {
                new RoundedPlateStep(
                    "plate",
                    OperationKind.Create,
                    request.Width,
                    request.Depth,
                    request.Thickness,
                    request.CornerRadius),
                new FourHolePatternStep(
                    "mounting-holes",
                    OperationKind.Create,
                    "plate",
                    request.HoleDiameter,
                    request.EdgeOffsetX,
                    request.EdgeOffsetY)
            },
            new DrawingOutput(request.Create3D, request.Create2D, request.AddDimensions),
            Array.Empty<PlanAssumption>());

        var validation = PlanValidator.Validate(plan);
        if (!validation.IsValid)
        {
            throw new PlanValidationException(validation.Errors);
        }

        return plan;
    }
}
