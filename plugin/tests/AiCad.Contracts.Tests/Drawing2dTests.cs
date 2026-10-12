using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace AiCad.Contracts.Tests;

public sealed class Drawing2dTests
{
    public const string ValidJson = """
        {"schemaVersion":"2d-1.0","requestId":"55555555-5555-5555-5555-555555555555",
         "documentFingerprint":"fingerprint-1","unit":"millimeter",
         "layers":[{"name":"HOLE","color":7,"lineType":"Continuous"}],
         "entities":[{"type":"circle","id":"bore","layer":"HOLE","center":{"x":110,"y":80},"radius":30}]}
        """;

    [Fact]
    public void ValidCircle_RoundTripsWithoutLosingGeometry()
    {
        var plan = Parse(ValidJson);
        var json = JsonSerializer.SerializeToNode(plan, plan.GetType(), PlanJson.Options)!;
        Assert.Equal(30, json["entities"]![0]!["radius"]!.GetValue<double>());
        Assert.Equal("circle", json["entities"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("millimeter", json["unit"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidRadius_IsRejected(double radius)
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"]![0]!["radius"] = radius;
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("1e309")]
    [InlineData("NaN")]
    [InlineData("-Infinity")]
    public void NonFiniteRadius_IsRejected(string value)
    {
        Assert.Throws<PlanValidationException>(() => Parse(ValidJson.Replace("\"radius\":30", $"\"radius\":{value}")));
    }

    [Fact]
    public void OversizedLayerList_IsRejected()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["layers"] = new JsonArray(Enumerable.Range(0, 33)
            .Select(i => (JsonNode?)new JsonObject { ["name"] = $"L{i}" }).ToArray());
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void OversizedEntityList_IsRejected()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"] = new JsonArray(Enumerable.Range(0, 501).Select(i =>
        {
            var circle = json["entities"]![0]!.DeepClone();
            circle["id"] = $"C{i}";
            return (JsonNode?)circle;
        }).ToArray());
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void UnknownEntityType_IsRejected()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"]![0]!["type"] = "execute_lisp";
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void UnrecognizedGeometryField_IsRejectedInsteadOfIgnored()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"]![0]!["raduis"] = 50;
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void DuplicateSemanticIds_AreRejected()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"]!.AsArray().Add(json["entities"]![0]!.DeepClone());
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void CoincidentLineEnds_AreRejected()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"] = JsonNode.Parse("""[{"type":"line","id":"zero","layer":"HOLE","start":{"x":2,"y":3},"end":{"x":2,"y":3}}]""");
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Fact]
    public void BulgedClosedPolyline_IsAccepted()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"] = JsonNode.Parse("""[{"type":"polyline","id":"slot","layer":"HOLE","closed":true,"vertices":[{"x":0,"y":-6,"bulge":0},{"x":18,"y":-6,"bulge":1},{"x":18,"y":6,"bulge":0},{"x":0,"y":6,"bulge":1}]}]""");
        Assert.NotNull(Parse(json.ToJsonString()));
    }

    [Fact]
    public void DiameterDimension_AllowsExplicitTextPosition()
    {
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"] = JsonNode.Parse("""[{"type":"diameter_dimension","id":"diameter","layer":"HOLE","chord":{"x":30,"y":0},"farChord":{"x":-30,"y":0},"leaderLength":10,"prefix":"6X ","textPosition":{"x":40,"y":15}}]""");
        var plan = Parse(json.ToJsonString());
        var result = JsonSerializer.SerializeToNode(plan, plan.GetType(), PlanJson.Options)!;
        Assert.Equal(40, result["entities"]![0]!["textPosition"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public void OversizedVertexList_IsRejected()
    {
        var vertices = new JsonArray(Enumerable.Range(0, 257).Select(i => (JsonNode?)new JsonObject { ["x"] = i, ["y"] = i % 2, ["bulge"] = 0 }).ToArray());
        var json = JsonNode.Parse(ValidJson)!;
        json["entities"] = new JsonArray(new JsonObject { ["type"] = "polyline", ["id"] = "too-large", ["layer"] = "HOLE", ["closed"] = false, ["vertices"] = vertices });
        Assert.Throws<PlanValidationException>(() => Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData(null, false, "fingerprint-1", "UNIT_REQUIRED")]
    [InlineData(LengthUnit.Inch, false, "fingerprint-1", "UNIT_MISMATCH")]
    [InlineData(LengthUnit.Millimeter, true, "fingerprint-1", "READ_ONLY_DOCUMENT")]
    [InlineData(LengthUnit.Millimeter, false, "changed", "DOCUMENT_CHANGED")]
    public void ContextMismatch_IsRejected(LengthUnit? unit, bool readOnly, string fingerprint, string code)
    {
        var plan = Parse(ValidJson);
        var context = new CadDocumentContext(fingerprint, "Drawing1.dwg", unit, readOnly, Array.Empty<double>(), Array.Empty<CadEntitySummary>());
        var error = Assert.ThrowsAny<Exception>(() => Invoke("Drawing2dValidator", "RequireContext", plan, context));
        Assert.Equal(code, error.GetType().GetProperty("Code")!.GetValue(error));
    }

    [Fact]
    public void ExistingOutput_IsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aicad-test-{Guid.NewGuid():N}.dwg");
        File.WriteAllText(path, "original");
        try
        {
            var error = Assert.ThrowsAny<Exception>(() => Invoke("DrawingFilePolicy", "Validate", path, null, null));
            Assert.Equal("OUTPUT_EXISTS", error.GetType().GetProperty("Code")!.GetValue(error));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("relative.dwg", null)]
    [InlineData("C:\\Temp\\output.exe", null)]
    [InlineData(null, null)]
    public void InvalidOutputPath_IsRejected(string? dwg, string? dxf)
    {
        var error = Assert.ThrowsAny<Exception>(() => Invoke("DrawingFilePolicy", "Validate", dwg, dxf, null));
        Assert.Equal("INVALID_OUTPUT_PATH", error.GetType().GetProperty("Code")!.GetValue(error));
    }

    private static object Parse(string json) => Invoke("Drawing2dPlan", "Parse", json)!;

    private static object? Invoke(string typeName, string methodName, params object?[] arguments)
    {
        var type = typeof(PlanJson).Assembly.GetType($"AiCad.Contracts.{typeName}");
        Assert.NotNull(type);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        try { return method.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
