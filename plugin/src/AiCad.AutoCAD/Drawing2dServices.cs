using System.Globalization;
using System.Text.Json;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AiCad.AutoCAD;

internal sealed class Drawing2dServices
{
    private readonly AutoCadContextReader _context = new();

    public DrawingEvidencePage<LayerEvidence> ReadLayers(Document document, DrawingEvidenceRequest request)
    {
        if (request.PageSize is < 1 or > 500 || (request.Cursor is not null && (!int.TryParse(request.Cursor, out var parsed) || parsed < 0)))
            throw new Drawing2dException("INVALID_PAGE", "Invalid layer page size or cursor.");
        var offset = request.Cursor is null ? 0 : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var table = (LayerTable)transaction.GetObject(document.Database.LayerTableId, OpenMode.ForRead);
        var all = table.Cast<ObjectId>().Select(id =>
        {
            var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            var line = (LinetypeTableRecord)transaction.GetObject(layer.LinetypeObjectId, OpenMode.ForRead);
            return new LayerEvidence(layer.Name, layer.Color.ColorIndex, line.Name, layer.IsLocked, layer.IsOff, layer.IsFrozen);
        }).OrderBy(layer => layer.Name, StringComparer.Ordinal).ToArray();
        var page = all.Skip(offset).Take(request.PageSize).ToArray();
        var next = offset + page.Length;
        return new(page, next < all.Length ? next.ToString(CultureInfo.InvariantCulture) : null, all.Length, _context.ComputeFingerprint(document));
    }

    public GeometryReadResult ReadGeometry(Document document, GeometryReadRequest request)
    {
        if (request.Handles is null || request.Handles.Count is < 1 or > 20)
            throw new Drawing2dException("INVALID_HANDLES", "Supply 1-20 handles.");
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var items = new List<GeometryEvidence>();
        foreach (var handle in request.Handles)
        {
            if (handle is null || handle.Length is < 1 or > 16 || !long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
                throw new Drawing2dException("INVALID_HANDLES", "Handles must be hexadecimal strings.");
            ObjectId id;
            try { id = document.Database.GetObjectId(false, new Handle(number), 0); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { throw new Drawing2dException("ENTITY_NOT_FOUND", $"No active entity for handle {handle}."); }
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                throw new Drawing2dException("ENTITY_NOT_FOUND", $"Handle {handle} is not an active entity.");
            object data = entity switch
            {
                Line e => new { start = P(e.StartPoint), end = P(e.EndPoint) },
                Circle e => new { center = P(e.Center), radius = e.Radius, normal = P(e.Normal) },
                Arc e => new { center = P(e.Center), radius = e.Radius, startAngleDegrees = Degrees(e.StartAngle), endAngleDegrees = Degrees(e.EndAngle),
                    start = P(e.StartPoint), end = P(e.EndPoint), normal = P(e.Normal) },
                Polyline e when e.NumberOfVertices <= 256 => new { closed = e.Closed, elevation = e.Elevation, normal = P(e.Normal),
                    vertices = Enumerable.Range(0, e.NumberOfVertices).Select(i => new PolylineVertex2(e.GetPoint2dAt(i).X, e.GetPoint2dAt(i).Y, e.GetBulgeAt(i))).ToArray() },
                Dimension e => new { measurement = e.Measurement, dimensionText = e.DimensionText, textPosition = P(e.TextPosition),
                    dimensionType = e.GetType().Name },
                _ => throw new Drawing2dException("UNSUPPORTED_ENTITY", "Inspect supports bounded 2D lines, circles, arcs, polylines and dimensions.")
            };
            items.Add(new(entity.Handle.ToString(), entity.GetType().Name, entity.Layer, JsonSerializer.SerializeToElement(data, PlanJson.Options)));
        }
        return new(items, _context.ComputeFingerprint(document));
    }

    public SaveDrawingResult Save(Document document, SaveDrawingRequest request)
    {
        using var documentLock = document.LockDocument();
        var context = _context.Read(document);
        if (context.IsReadOnly) throw new Drawing2dException("READ_ONLY_DOCUMENT", "The active drawing is read-only.");
        if (context.DocumentFingerprint != request.DocumentFingerprint)
            throw new Drawing2dException("DOCUMENT_CHANGED", "Refresh the drawing context before saving.");
        DrawingFilePolicy.Validate(request.DwgPath, request.DxfPath, document.Name);
        var pending = new List<(string Temporary, string Target)>();
        var published = new List<SavedDrawingFile>();
        try
        {
            foreach (var path in new[] { request.DwgPath, request.DxfPath }.Where(p => p is not null))
            {
                var target = Path.GetFullPath(path!);
                var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".aicad-{Guid.NewGuid():N}{Path.GetExtension(target)}");
                pending.Add((temporary, target));
                if (target.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                    document.Database.SaveAs(temporary, false, DwgVersion.AC1032, null);
                else
                    document.Database.DxfOut(temporary, 16, DwgVersion.AC1032);
                if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                    throw new Drawing2dException("SAVE_FAILED", "AutoCAD produced an empty output.");
            }
            foreach (var (temporary, target) in pending)
            {
                File.Move(temporary, target, overwrite: false);
                published.Add(new(target, new FileInfo(target).Length));
            }
            return new(true, published, _context.ComputeFingerprint(document));
        }
        catch (IOException)
        {
            throw new Drawing2dException("SAVE_FAILED", "Output could not be published. Check requested paths before retrying; an earlier output may exist.");
        }
        finally
        {
            foreach (var (temporary, _) in pending)
                if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Point3 P(Point3d point) => new(point.X, point.Y, point.Z);
    private static Point3 P(Vector3d point) => new(point.X, point.Y, point.Z);
    private static double Degrees(double radians) => radians * 180 / Math.PI;
}
