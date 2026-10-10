using System.Text.Json;
using AiCad.Contracts;
using Xunit;

namespace AiCad.Contracts.Tests;

public sealed class PlanJsonTests
{
    [Fact]
    public void RoundTrip_PreservesDerivedSteps()
    {
        var plan = CreatePlan();

        var json = JsonSerializer.Serialize(plan, PlanJson.Options);
        var copy = JsonSerializer.Deserialize<DrawingPlan>(json, PlanJson.Options);

        Assert.NotNull(copy);
        Assert.IsType<RoundedPlateStep>(copy.Steps[0]);
        Assert.IsType<FourHolePatternStep>(copy.Steps[1]);
        Assert.Contains("\"$type\":\"rounded_plate\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_IgnoresUnknownFutureFields()
    {
        var json = JsonSerializer.Serialize(CreatePlan(), PlanJson.Options);
        json = json.Insert(1, "\"futureField\":123,");

        var copy = JsonSerializer.Deserialize<DrawingPlan>(json, PlanJson.Options);

        Assert.NotNull(copy);
        Assert.Equal("1.0", copy.SchemaVersion);
    }

    [Fact]
    public void Deserialize_RejectsUnknownFeatureType()
    {
        var json = JsonSerializer.Serialize(CreatePlan(), PlanJson.Options)
            .Replace("rounded_plate", "arbitrary_command", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DrawingPlan>(json, PlanJson.Options));
    }

    [Fact]
    public void Deserialize_RejectsUnknownOperation()
    {
        var json = JsonSerializer.Serialize(CreatePlan(), PlanJson.Options)
            .Replace("\"create\"", "\"executeScript\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DrawingPlan>(json, PlanJson.Options));
    }

    private static DrawingPlan CreatePlan()
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
                new RoundedPlateStep("plate", OperationKind.Create, 100, 60, 10, 5),
                new FourHolePatternStep("mounting-holes", OperationKind.Create, "plate", 8, 10, 10)
            },
            new DrawingOutput(true, true, true),
            Array.Empty<PlanAssumption>());
    }
}
