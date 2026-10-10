using AiCad.Contracts;

namespace AiCad.Contracts.Tests;

internal static class PlanSamples
{
    public static DrawingPlan ValidCreate(
        double width = 100,
        double depth = 60,
        double thickness = 10,
        double cornerRadius = 5,
        double holeDiameter = 8,
        double edgeOffsetX = 10,
        double edgeOffsetY = 10)
    {
        return new DrawingPlan(
            "1.0",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "doc-1",
            LengthUnit.Millimeter,
            new Point3(0, 0, 0),
            OperationKind.Create,
            new FeatureStep[]
            {
                new RoundedPlateStep(
                    "plate", OperationKind.Create,
                    width, depth, thickness, cornerRadius),
                new FourHolePatternStep(
                    "mounting-holes", OperationKind.Create, "plate",
                    holeDiameter, edgeOffsetX, edgeOffsetY)
            },
            new DrawingOutput(true, true, true),
            Array.Empty<PlanAssumption>());
    }

    public static DrawingPlan WithOperation(OperationKind operation)
    {
        var source = ValidCreate();
        var steps = source.Steps
            .Select(step =>
            {
                FeatureStep updated = step switch
                {
                    RoundedPlateStep plate => plate with { Operation = operation },
                    FourHolePatternStep holes => holes with { Operation = operation },
                    _ => throw new InvalidOperationException("Unexpected sample step.")
                };
                return updated;
            })
            .ToArray();

        return source with { DeclaredOperation = operation, Steps = steps };
    }
}
