using System.Text.Json;
using AiCad.Contracts;
using AiCad.Mechanical;
using Xunit;

namespace AiCad.Mechanical.Tests;

public sealed class RoundedPlatePlanFactoryTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_ProducesPlateAndFourHoleFeature()
    {
        var request = ValidRequest();

        var plan = new RoundedPlatePlanFactory().Create(
            request,
            "doc-1",
            RequestId,
            LengthUnit.Millimeter);

        Assert.Equal(OperationKind.Create, plan.DeclaredOperation);
        Assert.Collection(
            plan.Steps,
            step =>
            {
                var plate = Assert.IsType<RoundedPlateStep>(step);
                Assert.Equal("plate", plate.SemanticId);
                Assert.Equal(100, plate.Width);
                Assert.Equal(60, plate.Depth);
                Assert.Equal(10, plate.Thickness);
                Assert.Equal(5, plate.CornerRadius);
            },
            step =>
            {
                var holes = Assert.IsType<FourHolePatternStep>(step);
                Assert.Equal("mounting-holes", holes.SemanticId);
                Assert.Equal("plate", holes.TargetSemanticId);
                Assert.Equal(8, holes.Diameter);
                Assert.Equal(10, holes.EdgeOffsetX);
                Assert.Equal(10, holes.EdgeOffsetY);
            });
        Assert.True(PlanValidator.Validate(plan).IsValid);
    }

    [Fact]
    public void Create_PreservesExplicitUnitOriginAndOutput()
    {
        var origin = new Point3(12, 34, 56);
        var request = ValidRequest() with
        {
            Create2D = true,
            Create3D = false,
            AddDimensions = false
        };

        var plan = new RoundedPlatePlanFactory().Create(
            request,
            "doc-inch",
            RequestId,
            LengthUnit.Inch,
            origin);

        Assert.Equal(LengthUnit.Inch, plan.Unit);
        Assert.Equal(origin, plan.Origin);
        Assert.Equal(new DrawingOutput(false, true, false), plan.Output);
        Assert.Empty(plan.Assumptions);
    }

    [Fact]
    public void Create_ProducesFourExpectedHoleCenters()
    {
        var plan = new RoundedPlatePlanFactory().Create(
            ValidRequest(),
            "doc-1",
            RequestId,
            LengthUnit.Millimeter);
        var plate = Assert.IsType<RoundedPlateStep>(plan.Steps[0]);
        var holes = Assert.IsType<FourHolePatternStep>(plan.Steps[1]);

        var centers = new[]
        {
            new Point3(holes.EdgeOffsetX, holes.EdgeOffsetY, 0),
            new Point3(plate.Width - holes.EdgeOffsetX, holes.EdgeOffsetY, 0),
            new Point3(holes.EdgeOffsetX, plate.Depth - holes.EdgeOffsetY, 0),
            new Point3(plate.Width - holes.EdgeOffsetX, plate.Depth - holes.EdgeOffsetY, 0)
        };

        Assert.Equal(
            new[]
            {
                new Point3(10, 10, 0),
                new Point3(90, 10, 0),
                new Point3(10, 50, 0),
                new Point3(90, 50, 0)
            },
            centers);
    }

    [Fact]
    public void Create_IsDeterministicForTheSameInputs()
    {
        var factory = new RoundedPlatePlanFactory();

        var first = factory.Create(ValidRequest(), "doc-1", RequestId, LengthUnit.Millimeter);
        var second = factory.Create(ValidRequest(), "doc-1", RequestId, LengthUnit.Millimeter);

        Assert.Equal(
            JsonSerializer.Serialize(first, PlanJson.Options),
            JsonSerializer.Serialize(second, PlanJson.Options));
    }

    [Theory]
    [InlineData(0, 60, 10, 5, 8, 10, 10)]
    [InlineData(100, 60, -1, 5, 8, 10, 10)]
    [InlineData(100, 60, 10, 31, 8, 10, 10)]
    [InlineData(100, 60, 10, 5, 80, 10, 10)]
    public void Create_RejectsInvalidRequests(
        double width,
        double depth,
        double thickness,
        double cornerRadius,
        double holeDiameter,
        double edgeOffsetX,
        double edgeOffsetY)
    {
        var request = new RoundedPlateRequest(
            width,
            depth,
            thickness,
            cornerRadius,
            holeDiameter,
            edgeOffsetX,
            edgeOffsetY,
            true,
            true,
            true);

        var exception = Assert.Throws<PlanValidationException>(() =>
            new RoundedPlatePlanFactory().Create(
                request,
                "doc-1",
                RequestId,
                LengthUnit.Millimeter));

        Assert.NotEmpty(exception.Errors);
    }

    private static RoundedPlateRequest ValidRequest()
    {
        return new RoundedPlateRequest(100, 60, 10, 5, 8, 10, 10, true, true, true);
    }
}
