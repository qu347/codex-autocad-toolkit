using System.Text.Json;
using AiCad.Contracts;
using Xunit;

namespace AiCad.Contracts.Tests;

public sealed class CadContextTests
{
    [Fact]
    public void HealthJsonRoundTrip_PreservesProtocolAndProcess()
    {
        var health = new HealthResult("1.0", "1.0.0", 1234);

        var json = JsonSerializer.Serialize(health, PlanJson.Options);
        var copy = JsonSerializer.Deserialize<HealthResult>(json, PlanJson.Options);

        Assert.Equal(health, copy);
    }

    [Fact]
    public void CreateResultJsonRoundTrip_PreservesBatchHandlesAndWarnings()
    {
        var result = new CreateResult(
            true,
            "batch-1",
            new Dictionary<string, string> { ["plate-solid"] = "2A" },
            new[] { "warning" },
            25);

        var json = JsonSerializer.Serialize(result, PlanJson.Options);
        var copy = JsonSerializer.Deserialize<CreateResult>(json, PlanJson.Options);

        Assert.NotNull(copy);
        Assert.True(copy.Success);
        Assert.Equal("batch-1", copy.BatchId);
        Assert.Equal("2A", copy.Handles["plate-solid"]);
        Assert.Equal("warning", Assert.Single(copy.Warnings));
        Assert.Equal(25, copy.ElapsedMilliseconds);
    }

    [Fact]
    public void JsonRoundTrip_PreservesDocumentAndSelectionSummary()
    {
        var context = new CadDocumentContext(
            "fingerprint-1",
            "part.dwg",
            LengthUnit.Millimeter,
            false,
            new double[]
            {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                12, 34, 56, 1
            },
            new[]
            {
                new CadEntitySummary(
                    "2A",
                    "Solid3d",
                    new Point3(0, 0, 0),
                    new Point3(100, 60, 10))
            });

        var json = JsonSerializer.Serialize(context, PlanJson.Options);
        var copy = JsonSerializer.Deserialize<CadDocumentContext>(json, PlanJson.Options);

        Assert.NotNull(copy);
        Assert.Equal("fingerprint-1", copy.DocumentFingerprint);
        Assert.Equal("part.dwg", copy.DrawingName);
        Assert.Equal(LengthUnit.Millimeter, copy.Unit);
        Assert.False(copy.IsReadOnly);
        Assert.Equal(16, copy.CurrentUcs.Count);
        Assert.Equal(12, copy.CurrentUcs[12]);
        Assert.Equal(34, copy.CurrentUcs[13]);
        Assert.Equal(56, copy.CurrentUcs[14]);
        var entity = Assert.Single(copy.Selection);
        Assert.Equal("2A", entity.Handle);
        Assert.Equal("Solid3d", entity.Type);
        Assert.Equal(new Point3(0, 0, 0), entity.Min);
        Assert.Equal(new Point3(100, 60, 10), entity.Max);
    }
}
