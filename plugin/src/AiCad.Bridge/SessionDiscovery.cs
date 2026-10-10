using System.Diagnostics;
using System.Text.Json;
using AiCad.Contracts;

namespace AiCad.Bridge;

public sealed class SessionDiscovery
{
    private readonly string _sessionDirectory;
    private readonly Func<int, bool> _isProcessAlive;
    private readonly Action<string>? _diagnostics;

    public SessionDiscovery(
        string sessionDirectory,
        Func<int, bool>? isProcessAlive = null,
        Action<string>? diagnostics = null)
    {
        _sessionDirectory = sessionDirectory;
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
        _diagnostics = diagnostics;
    }

    public CadSessionDescriptor FindActive()
    {
        if (!Directory.Exists(_sessionDirectory))
        {
            throw NoSession();
        }

        var candidates = new List<CadSessionDescriptor>();
        foreach (var path in Directory.EnumerateFiles(_sessionDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var descriptor = JsonSerializer.Deserialize<CadSessionDescriptor>(
                    File.ReadAllText(path),
                    PlanJson.Options);
                if (descriptor is not null && IsUsable(descriptor))
                {
                    candidates.Add(descriptor);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                _diagnostics?.Invoke($"Skipped unusable session descriptor '{Path.GetFileName(path)}': {exception.GetType().Name}");
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.StartedAt)
            .FirstOrDefault()
            ?? throw NoSession();
    }

    private bool IsUsable(CadSessionDescriptor descriptor)
    {
        return descriptor.ProcessId > 0
            && descriptor.PipeName is { Length: > 0 } pipeName
            && pipeName.StartsWith("aicad-", StringComparison.Ordinal)
            && descriptor.SessionToken is { Length: >= 32 }
            && !string.IsNullOrWhiteSpace(descriptor.AutoCadVersion)
            && _isProcessAlive(descriptor.ProcessId);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CadBridgeException NoSession()
    {
        return new CadBridgeException("NO_AUTOCAD_SESSION", "No active AutoCAD AI-CAD session was found.");
    }
}
