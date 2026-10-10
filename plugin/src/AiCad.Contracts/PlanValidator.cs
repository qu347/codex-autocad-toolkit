namespace AiCad.Contracts;

public sealed record PlanError(string Code, string Path, string Message);

public sealed record PlanValidationResult(IReadOnlyList<PlanError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class PlanValidationException : Exception
{
    public PlanValidationException(IReadOnlyList<PlanError> errors)
        : base("The drawing plan is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyList<PlanError> Errors { get; }
}

public static class PlanValidator
{
    private const double MaxCoordinate = 1_000_000_000;
    private const double MaxDimension = 10_000_000;
    private const int MaxSteps = 100;

    public static PlanValidationResult Validate(DrawingPlan plan)
    {
        var errors = new List<PlanError>();

        if (!string.Equals(plan.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            Add(errors, "UNSUPPORTED_SCHEMA", "schemaVersion", "Only DrawingPlan schema 1.0 is supported.");
        }

        if (plan.RequestId == Guid.Empty)
        {
            Add(errors, "REQUEST_ID_REQUIRED", "requestId", "A non-empty request identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(plan.DocumentFingerprint))
        {
            Add(errors, "DOCUMENT_FINGERPRINT_REQUIRED", "documentFingerprint", "A document fingerprint is required.");
        }

        if (!Enum.IsDefined(plan.Unit))
        {
            Add(errors, "UNSUPPORTED_UNIT", "unit", "The drawing length unit is not supported.");
        }

        if (!Enum.IsDefined(plan.DeclaredOperation))
        {
            Add(errors, "UNSUPPORTED_OPERATION", "declaredOperation", "The declared operation kind is not supported.");
        }

        if (!IsCoordinate(plan.Origin.X) || !IsCoordinate(plan.Origin.Y) || !IsCoordinate(plan.Origin.Z))
        {
            Add(errors, "ORIGIN_OUT_OF_RANGE", "origin", "Origin coordinates must be finite and within the supported range.");
        }

        if (!plan.Output.Create2D && !plan.Output.Create3D)
        {
            Add(errors, "OUTPUT_REQUIRED", "output", "At least one of the 2D or 3D outputs must be enabled.");
        }

        if (plan.Steps.Count == 0)
        {
            Add(errors, "STEPS_REQUIRED", "steps", "At least one feature step is required.");
        }
        else if (plan.Steps.Count > MaxSteps)
        {
            Add(errors, "STEP_LIMIT_EXCEEDED", "steps", $"A plan may contain at most {MaxSteps} feature steps.");
        }

        ValidateSteps(plan.Steps, errors);

        var actualOperation = Classify(plan.Steps);
        if (actualOperation != plan.DeclaredOperation)
        {
            Add(
                errors,
                "OPERATION_CLASSIFICATION_MISMATCH",
                "declaredOperation",
                $"The declared operation {plan.DeclaredOperation} does not match the actual operation {actualOperation}.");
        }

        return new PlanValidationResult(errors);
    }

    public static OperationKind Classify(IReadOnlyList<FeatureStep> steps)
    {
        if (steps.Count == 0)
        {
            return OperationKind.Mixed;
        }

        var first = steps[0].Operation;
        return steps.Skip(1).Any(step => step.Operation != first)
            ? OperationKind.Mixed
            : first;
    }

    private static void ValidateSteps(IReadOnlyList<FeatureStep> steps, List<PlanError> errors)
    {
        var semanticIds = new HashSet<string>(StringComparer.Ordinal);
        var plates = new Dictionary<string, RoundedPlateStep>(StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var path = $"steps[{index}]";

            if (!Enum.IsDefined(step.Operation))
            {
                Add(errors, "UNSUPPORTED_OPERATION", $"{path}.operation", "The feature operation kind is not supported.");
            }

            if (string.IsNullOrWhiteSpace(step.SemanticId))
            {
                Add(errors, "SEMANTIC_ID_REQUIRED", $"{path}.semanticId", "Every feature step requires a semantic identifier.");
            }
            else if (!semanticIds.Add(step.SemanticId))
            {
                Add(errors, "DUPLICATE_SEMANTIC_ID", $"{path}.semanticId", $"Semantic identifier '{step.SemanticId}' is duplicated.");
            }

            switch (step)
            {
                case RoundedPlateStep plate:
                    ValidatePlate(plate, path, errors);
                    if (!string.IsNullOrWhiteSpace(plate.SemanticId))
                    {
                        plates.TryAdd(plate.SemanticId, plate);
                    }

                    break;

                case FourHolePatternStep holes:
                    ValidateHoleDimensions(holes, path, errors);
                    if (!plates.TryGetValue(holes.TargetSemanticId, out var target))
                    {
                        Add(errors, "FEATURE_TARGET_NOT_FOUND", $"{path}.targetSemanticId", "The hole pattern target must be a preceding rounded plate step.");
                    }
                    else if (HasValidPlateDimensions(target) && HasValidHoleDimensions(holes))
                    {
                        ValidateHoleFit(target, holes, path, errors);
                    }

                    break;
            }
        }
    }

    private static void ValidatePlate(RoundedPlateStep plate, string path, List<PlanError> errors)
    {
        if (!IsPositiveDimension(plate.Width)
            || !IsPositiveDimension(plate.Depth)
            || !IsPositiveDimension(plate.Thickness)
            || !IsNonNegativeDimension(plate.CornerRadius))
        {
            Add(errors, "FEATURE_DIMENSION_OUT_OF_RANGE", path, "Plate dimensions must be finite, positive, and within the supported range; corner radius may be zero.");
            return;
        }

        if (plate.CornerRadius > Math.Min(plate.Width, plate.Depth) / 2)
        {
            Add(errors, "CORNER_RADIUS_OUT_OF_RANGE", $"{path}.cornerRadius", "Corner radius cannot exceed half of the smaller plate side.");
        }
    }

    private static void ValidateHoleDimensions(FourHolePatternStep holes, string path, List<PlanError> errors)
    {
        if (!HasValidHoleDimensions(holes))
        {
            Add(errors, "FEATURE_DIMENSION_OUT_OF_RANGE", path, "Hole diameter and edge offsets must be finite, positive, and within the supported range.");
        }
    }

    private static void ValidateHoleFit(
        RoundedPlateStep plate,
        FourHolePatternStep holes,
        string path,
        List<PlanError> errors)
    {
        var centers = new[]
        {
            (X: holes.EdgeOffsetX, Y: holes.EdgeOffsetY),
            (X: plate.Width - holes.EdgeOffsetX, Y: holes.EdgeOffsetY),
            (X: holes.EdgeOffsetX, Y: plate.Depth - holes.EdgeOffsetY),
            (X: plate.Width - holes.EdgeOffsetX, Y: plate.Depth - holes.EdgeOffsetY)
        };
        var holeRadius = holes.Diameter / 2;

        if (centers.Any(center => !CircleFitsRoundedRectangle(
                center.X,
                center.Y,
                holeRadius,
                plate.Width,
                plate.Depth,
                plate.CornerRadius)))
        {
            Add(errors, "HOLE_OUTSIDE_PLATE", path, "Every hole must fit completely inside the rounded plate profile.");
        }
    }

    private static bool CircleFitsRoundedRectangle(
        double x,
        double y,
        double holeRadius,
        double width,
        double depth,
        double cornerRadius)
    {
        if (x - holeRadius < 0
            || x + holeRadius > width
            || y - holeRadius < 0
            || y + holeRadius > depth)
        {
            return false;
        }

        if (cornerRadius == 0)
        {
            return true;
        }

        var cornerX = x < cornerRadius
            ? cornerRadius
            : x > width - cornerRadius
                ? width - cornerRadius
                : double.NaN;
        var cornerY = y < cornerRadius
            ? cornerRadius
            : y > depth - cornerRadius
                ? depth - cornerRadius
                : double.NaN;

        if (double.IsNaN(cornerX) || double.IsNaN(cornerY))
        {
            return true;
        }

        var availableRadius = cornerRadius - holeRadius;
        if (availableRadius < 0)
        {
            return false;
        }

        var deltaX = x - cornerX;
        var deltaY = y - cornerY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= availableRadius * availableRadius;
    }

    private static bool HasValidPlateDimensions(RoundedPlateStep plate)
    {
        return IsPositiveDimension(plate.Width)
            && IsPositiveDimension(plate.Depth)
            && IsPositiveDimension(plate.Thickness)
            && IsNonNegativeDimension(plate.CornerRadius)
            && plate.CornerRadius <= Math.Min(plate.Width, plate.Depth) / 2;
    }

    private static bool HasValidHoleDimensions(FourHolePatternStep holes)
    {
        return IsPositiveDimension(holes.Diameter)
            && IsPositiveDimension(holes.EdgeOffsetX)
            && IsPositiveDimension(holes.EdgeOffsetY);
    }

    private static bool IsCoordinate(double value)
    {
        return double.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;
    }

    private static bool IsPositiveDimension(double value)
    {
        return double.IsFinite(value) && value > 0 && value <= MaxDimension;
    }

    private static bool IsNonNegativeDimension(double value)
    {
        return double.IsFinite(value) && value >= 0 && value <= MaxDimension;
    }

    private static void Add(List<PlanError> errors, string code, string path, string message)
    {
        errors.Add(new PlanError(code, path, message));
    }
}
