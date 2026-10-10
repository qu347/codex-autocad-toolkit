using System.Text.Json;
using AiCad.Bridge;
using AiCad.Contracts;
using Xunit;

namespace AiCad.Bridge.Tests;

public sealed class SessionDiscoveryTests
{
    [Fact]
    public void FindActive_SelectsNewestLiveDescriptor()
    {
        using var fixture = new SessionFixture();
        fixture.Write("old.json", Descriptor(10, "aicad-old", StartedAt: new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero)));
        fixture.Write("new.json", Descriptor(20, "aicad-new", StartedAt: new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero)));

        var found = new SessionDiscovery(fixture.DirectoryPath, processId => processId is 10 or 20).FindActive();

        Assert.Equal(20, found.ProcessId);
        Assert.Equal("aicad-new", found.PipeName);
    }

    [Fact]
    public void FindActive_SkipsMalformedDeadAndInvalidDescriptors()
    {
        using var fixture = new SessionFixture();
        fixture.WriteRaw("malformed.json", "not-json");
        fixture.Write("dead.json", Descriptor(30, "aicad-dead"));
        fixture.Write("short-token.json", Descriptor(40, "aicad-short", SessionToken: "short"));
        fixture.Write("wrong-pipe.json", Descriptor(50, "other-pipe"));
        fixture.WriteRaw("null-token.json", "{\"processId\":55,\"pipeName\":\"aicad-null\",\"sessionToken\":null,\"autoCadVersion\":\"25.1\",\"startedAt\":\"2026-09-09T08:00:00Z\"}");
        fixture.Write("live.json", Descriptor(60, "aicad-live"));

        var found = new SessionDiscovery(fixture.DirectoryPath, processId => processId == 60).FindActive();

        Assert.Equal(60, found.ProcessId);
        Assert.Equal("aicad-live", found.PipeName);
    }

    [Fact]
    public void FindActive_WhenDirectoryDoesNotExist_ReturnsStableError()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"aicad-missing-{Guid.NewGuid():N}");
        var discovery = new SessionDiscovery(missing, _ => true);

        var exception = Assert.Throws<CadBridgeException>(() => discovery.FindActive());

        Assert.Equal("NO_AUTOCAD_SESSION", exception.Code);
    }

    [Fact]
    public void FindActive_WhenNoDescriptorIsUsable_ReturnsStableError()
    {
        using var fixture = new SessionFixture();
        fixture.Write("dead.json", Descriptor(30, "aicad-dead"));

        var exception = Assert.Throws<CadBridgeException>(() =>
            new SessionDiscovery(fixture.DirectoryPath, _ => false).FindActive());

        Assert.Equal("NO_AUTOCAD_SESSION", exception.Code);
    }

    private static CadSessionDescriptor Descriptor(
        int ProcessId,
        string PipeName,
        string SessionToken = "abcdefghijklmnopqrstuvwxyz123456",
        DateTimeOffset? StartedAt = null)
    {
        return new CadSessionDescriptor(
            ProcessId,
            PipeName,
            SessionToken,
            "25.1.60.0.0",
            StartedAt ?? new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero));
    }

    private sealed class SessionFixture : IDisposable
    {
        public SessionFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"aicad-session-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Write(string fileName, CadSessionDescriptor descriptor)
        {
            WriteRaw(fileName, JsonSerializer.Serialize(descriptor, PlanJson.Options));
        }

        public void WriteRaw(string fileName, string content)
        {
            File.WriteAllText(Path.Combine(DirectoryPath, fileName), content);
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
