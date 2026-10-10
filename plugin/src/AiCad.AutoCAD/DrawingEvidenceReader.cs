using System.Collections;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AiCad.AutoCAD;

internal sealed class DrawingEvidenceException : System.Exception
{
    public DrawingEvidenceException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

internal sealed class DrawingEvidenceReader
{
    private const int MaximumPageSize = 500;
    private readonly AutoCadContextReader _context = new();

    public DrawingEvidencePage<LayoutEvidence> ReadLayouts(Document document, DrawingEvidenceRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var dictionary = (DBDictionary)transaction.GetObject(document.Database.LayoutDictionaryId, OpenMode.ForRead);
        var items = new List<LayoutEvidence>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Value is not ObjectId layoutId)
            {
                continue;
            }

            var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForRead);
            var record = (BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            items.Add(new LayoutEvidence(layout.LayoutName, layout.ModelType, record.Cast<ObjectId>().Count()));
        }

        return Page(items.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray(), request, document);
    }

    public DrawingEvidencePage<EntityEvidence> ReadEntities(Document document, DrawingEvidenceRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var items = Entities(document, transaction, request)
            .Select(entity =>
            {
                var (minimum, maximum) = Extents(entity);
                return new EntityEvidence(
                    entity.Handle.ToString(),
                    entity.GetType().Name,
                    entity.Layer,
                    minimum,
                    maximum);
            })
            .ToArray();
        return Page(items, request, document);
    }

    public DrawingEvidencePage<DimensionEvidence> ReadDimensions(Document document, DrawingEvidenceRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var items = Entities(document, transaction, request)
            .OfType<Dimension>()
            .Select(dimension => new DimensionEvidence(
                dimension.Handle.ToString(),
                dimension.GetType().Name,
                dimension.Layer,
                dimension.Measurement,
                dimension.DimensionText ?? "",
                Point(dimension.TextPosition)))
            .ToArray();
        return Page(items, request, document);
    }

    public DrawingEvidencePage<TextTableEvidence> ReadTextAndTables(Document document, DrawingEvidenceRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var items = Entities(document, transaction, request)
            .Where(entity => entity is DBText or MText or Table)
            .Select(entity => entity switch
            {
                DBText text => new TextTableEvidence(
                    text.Handle.ToString(),
                    nameof(DBText),
                    text.Layer,
                    text.TextString ?? "",
                    null,
                    null,
                    Point(text.Position)),
                MText text => new TextTableEvidence(
                    text.Handle.ToString(),
                    nameof(MText),
                    text.Layer,
                    text.Contents ?? "",
                    null,
                    null,
                    Point(text.Location)),
                Table table => new TextTableEvidence(
                    table.Handle.ToString(),
                    nameof(Table),
                    table.Layer,
                    TableText(table),
                    table.Rows.Count,
                    table.Columns.Count,
                    Point(table.Position)),
                _ => throw new InvalidOperationException("Unexpected text evidence type.")
            })
            .ToArray();
        return Page(items, request, document);
    }

    public DrawingEvidencePage<BlockEvidence> ReadBlocks(Document document, DrawingEvidenceRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var items = Entities(document, transaction, request)
            .OfType<BlockReference>()
            .Select(reference => new BlockEvidence(
                reference.Handle.ToString(),
                BlockName(reference, transaction),
                reference.Layer,
                Point(reference.Position),
                reference.Rotation,
                Attributes(reference, transaction)))
            .ToArray();
        return Page(items, request, document);
    }

    private static IReadOnlyList<Entity> Entities(
        Document document,
        Transaction transaction,
        DrawingEvidenceRequest request)
    {
        var record = LayoutRecord(document.Database, transaction, request.Layout);
        return record.Cast<ObjectId>()
            .Select(id => transaction.GetObject(id, OpenMode.ForRead, false))
            .OfType<Entity>()
            .OrderBy(entity => entity.Handle.Value)
            .ToArray();
    }

    private static BlockTableRecord LayoutRecord(Database database, Transaction transaction, string? layoutName)
    {
        if (string.IsNullOrWhiteSpace(layoutName))
        {
            throw new DrawingEvidenceException("INVALID_LAYOUT", "layout is required.");
        }

        var dictionary = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
        if (!dictionary.Contains(layoutName))
        {
            throw new DrawingEvidenceException("INVALID_LAYOUT", $"Layout '{layoutName}' does not exist.");
        }

        var layout = (Layout)transaction.GetObject(dictionary.GetAt(layoutName), OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
    }

    private DrawingEvidencePage<T> Page<T>(IReadOnlyList<T> items, DrawingEvidenceRequest request, Document document)
    {
        if (request.PageSize < 1 || request.PageSize > MaximumPageSize)
        {
            throw new DrawingEvidenceException("INVALID_PAGE_SIZE", $"pageSize must be from 1 through {MaximumPageSize}.");
        }

        var offset = 0;
        if (request.Cursor is not null && (!int.TryParse(request.Cursor, out offset) || offset < 0 || offset > items.Count))
        {
            throw new DrawingEvidenceException("INVALID_CURSOR", "cursor is invalid for the current result set.");
        }

        var page = items.Skip(offset).Take(request.PageSize).ToArray();
        var next = offset + page.Length < items.Count ? (offset + page.Length).ToString() : null;
        return new DrawingEvidencePage<T>(page, next, items.Count, _context.ComputeFingerprint(document));
    }

    private static (Point3? Minimum, Point3? Maximum) Extents(Entity entity)
    {
        try
        {
            var extents = entity.GeometricExtents;
            return (Point(extents.MinPoint), Point(extents.MaxPoint));
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return (null, null);
        }
    }

    private static string BlockName(BlockReference reference, Transaction transaction)
    {
        var id = reference.IsDynamicBlock ? reference.DynamicBlockTableRecord : reference.BlockTableRecord;
        return ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name;
    }

    private static IReadOnlyDictionary<string, string> Attributes(BlockReference reference, Transaction transaction)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ObjectId id in reference.AttributeCollection)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is AttributeReference attribute)
            {
                attributes[attribute.Tag ?? ""] = attribute.TextString ?? "";
            }
        }

        return attributes;
    }

    private static string TableText(Table table)
    {
        var values = new List<string>();
        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.Columns.Count; column++)
            {
                values.Add(table.Cells[row, column].TextString ?? "");
            }
        }

        return string.Join("\t", values);
    }

    private static Point3 Point(Autodesk.AutoCAD.Geometry.Point3d point) => new(point.X, point.Y, point.Z);
}
