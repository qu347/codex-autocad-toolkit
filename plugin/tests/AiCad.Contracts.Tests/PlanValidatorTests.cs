using AiCad.Contracts;
using Xunit;

namespace AiCad.Contracts.Tests;

public sealed class PlanValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidRoundedPlate()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_RejectsUnknownSchemaVersion()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate() with { SchemaVersion = "2.0" });

        AssertError(result, "UNSUPPORTED_SCHEMA");
    }

    [Fact]
    public void Validate_RejectsMissingRequestIdentity()
    {
        var plan = PlanSamples.ValidCreate() with
        {
            RequestId = Guid.Empty,
            DocumentFingerprint = " "
        };

        var result = PlanValidator.Validate(plan);

        AssertError(result, "REQUEST_ID_REQUIRED");
        AssertError(result, "DOCUMENT_FINGERPRINT_REQUIRED");
    }

    [Fact]
    public void Validate_RejectsUndefinedLengthUnit()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate() with
        {
            Unit = (LengthUnit)999
        });

        AssertError(result, "UNSUPPORTED_UNIT");
    }

    [Fact]
    public void Validate_RejectsUndefinedOperationKind()
    {
        var result = PlanValidator.Validate(PlanSamples.WithOperation((OperationKind)999));

        AssertError(result, "UNSUPPORTED_OPERATION");
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(0, 0, double.NegativeInfinity)]
    [InlineData(1_000_000_001, 0, 0)]
    public void Validate_RejectsNonFiniteOrExcessiveOrigin(double x, double y, double z)
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate() with { Origin = new Point3(x, y, z) });

        AssertError(result, "ORIGIN_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(0, 60, 10, 5, 8, 10, 10)]
    [InlineData(100, -1, 10, 5, 8, 10, 10)]
    [InlineData(100, 60, 0, 5, 8, 10, 10)]
    [InlineData(100, 60, 10, -1, 8, 10, 10)]
    [InlineData(100, 60, 10, 5, 0, 10, 10)]
    [InlineData(100, 60, 10, 5, 8, 0, 10)]
    [InlineData(100, 60, 10, 5, 8, 10, -1)]
    public void Validate_RejectsInvalidFeatureDimensions(
        double width,
        double depth,
        double thickness,
        double cornerRadius,
        double holeDiameter,
        double edgeOffsetX,
        double edgeOffsetY)
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate(
            width, depth, thickness, cornerRadius,
            holeDiameter, edgeOffsetX, edgeOffsetY));

        AssertError(result, "FEATURE_DIMENSION_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(10_000_001)]
    public void Validate_RejectsNonFiniteOrExcessiveFeatureDimension(double width)
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate(width: width));

        AssertError(result, "FEATURE_DIMENSION_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(100, 60, 31)]
    [InlineData(10, 20, 6)]
    public void Validate_RejectsOversizedCornerRadius(double width, double depth, double radius)
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate(width, depth, cornerRadius: radius));

        AssertError(result, "CORNER_RADIUS_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(3, 10)]
    [InlineData(98, 10)]
    [InlineData(10, 2)]
    [InlineData(10, 59)]
    public void Validate_RejectsHoleOutsidePlate(double edgeOffsetX, double edgeOffsetY)
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate(
            holeDiameter: 8,
            edgeOffsetX: edgeOffsetX,
            edgeOffsetY: edgeOffsetY));

        AssertError(result, "HOLE_OUTSIDE_PLATE");
    }

    [Fact]
    public void Validate_RejectsHoleThatCutsRoundedCorner()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate(
            cornerRadius: 10,
            holeDiameter: 2,
            edgeOffsetX: 1,
            edgeOffsetY: 1));

        AssertError(result, "HOLE_OUTSIDE_PLATE");
    }

    [Fact]
    public void Validate_RejectsUnknownFeatureTarget()
    {
        var plan = PlanSamples.ValidCreate();
        var holes = (FourHolePatternStep)plan.Steps[1];
        plan = plan with { Steps = new FeatureStep[] { plan.Steps[0], holes with { TargetSemanticId = "missing" } } };

        var result = PlanValidator.Validate(plan);

        AssertError(result, "FEATURE_TARGET_NOT_FOUND");
    }

    [Fact]
    public void Validate_RejectsDuplicateSemanticIds()
    {
        var plan = PlanSamples.ValidCreate();
        var holes = (FourHolePatternStep)plan.Steps[1];
        plan = plan with { Steps = new FeatureStep[] { plan.Steps[0], holes with { SemanticId = "plate" } } };

        var result = PlanValidator.Validate(plan);

        AssertError(result, "DUPLICATE_SEMANTIC_ID");
    }

    [Fact]
    public void Validate_RejectsEmptyOutput()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate() with
        {
            Output = new DrawingOutput(false, false, false)
        });

        AssertError(result, "OUTPUT_REQUIRED");
    }

    [Fact]
    public void Validate_RejectsOperationClassificationMismatch()
    {
        var result = PlanValidator.Validate(PlanSamples.ValidCreate() with
        {
            DeclaredOperation = OperationKind.Modify
        });

        AssertError(result, "OPERATION_CLASSIFICATION_MISMATCH");
    }

    [Fact]
    public void Validate_RejectsMixedStepsDeclaredAsCreate()
    {
        var plan = PlanSamples.ValidCreate();
        var holes = (FourHolePatternStep)plan.Steps[1];
        plan = plan with
        {
            Steps = new FeatureStep[] { plan.Steps[0], holes with { Operation = OperationKind.Modify } }
        };

        var result = PlanValidator.Validate(plan);

        AssertError(result, "OPERATION_CLASSIFICATION_MISMATCH");
    }

    [Fact]
    public void Validate_RejectsMoreThanOneHundredSteps()
    {
        var plan = PlanSamples.ValidCreate() with
        {
            Steps = Enumerable.Range(1, 101)
                .Select(index => (FeatureStep)new RoundedPlateStep(
                    $"plate-{index}", OperationKind.Create, 100, 60, 10, 5))
                .ToArray()
        };

        var result = PlanValidator.Validate(plan);

        AssertError(result, "STEP_LIMIT_EXCEEDED");
    }

    private static void AssertError(PlanValidationResult result, string code)
    {
        Assert.Contains(result.Errors, error => error.Code == code);
    }
}
