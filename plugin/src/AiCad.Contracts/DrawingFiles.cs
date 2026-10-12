namespace AiCad.Contracts;

public static class DrawingFilePolicy
{
    public static void Validate(string? dwgPath, string? dxfPath, string? sourcePath)
    {
        if (dwgPath is null && dxfPath is null)
            throw new Drawing2dException("INVALID_OUTPUT_PATH", "Supply a new absolute DWG or DXF path.");
        Check(dwgPath, ".dwg", sourcePath);
        Check(dxfPath, ".dxf", sourcePath);
    }

    private static void Check(string? path, string extension, string? sourcePath)
    {
        if (path is null) return;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)
            || path.Any(char.IsControl))
            throw new Drawing2dException("INVALID_OUTPUT_PATH", $"Supply an absolute {extension} file path.");
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new Drawing2dException("INVALID_OUTPUT_PATH", "The output path is invalid."); }
        if (sourcePath is not null && Path.IsPathFullyQualified(sourcePath)
            && full.Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            throw new Drawing2dException("OUTPUT_EXISTS", "The source drawing cannot be overwritten.");
        if (File.Exists(full) || Directory.Exists(full))
            throw new Drawing2dException("OUTPUT_EXISTS", "Choose a new path; the output already exists.");
        if (!Directory.Exists(Path.GetDirectoryName(full)))
            throw new Drawing2dException("OUTPUT_DIRECTORY_MISSING", "Create the output directory before saving.");
    }
}
