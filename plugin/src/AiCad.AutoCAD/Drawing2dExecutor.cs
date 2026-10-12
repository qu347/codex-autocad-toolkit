using System.Diagnostics;
using System.Globalization;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Entity2d = AiCad.Contracts.Entity2d;
using Polyline2d = AiCad.Contracts.Polyline2d;
using Line2d = AiCad.Contracts.Line2d;

namespace AiCad.AutoCAD;

internal sealed class Drawing2dExecutor
{
    private readonly AutoCadContextReader _context = new();
    private readonly BatchMetadataStore _metadata = new();

    public CreateResult ExecuteCreate(Document document, Drawing2dPlan plan)
    {
        var validation = Drawing2dValidator.Validate(plan);
        if (!validation.IsValid) throw new PlanValidationException(validation.Errors);
        using var documentLock = document.LockDocument();
        var canonical = plan.ToJson();
        using (var lookup = document.Database.TransactionManager.StartTransaction())
        {
            var prior = _metadata.FindByRequest(document.Database, lookup, plan.RequestId);
            if (prior is not null)
            {
                if (prior.SchemaVersion != "2d-1.0" || prior.PlanJson != canonical)
                    throw new Drawing2dException("REQUEST_ID_CONFLICT", "This requestId belongs to a different plan; use a new GUID for a new intent.");
                if (prior.IsUndone) throw new Drawing2dException("REQUEST_UNDONE", "This request was undone. Use a new GUID for a new drawing stage.");
                var current = _context.Read(document);
                Drawing2dValidator.RequireContext(plan with { DocumentFingerprint = current.DocumentFingerprint }, current);
                var actual = CaptureSnapshots(document.Database, lookup, prior.Handles);
                if (prior.EntitySnapshots is null || !prior.EntitySnapshots.OrderBy(p => p.Key).SequenceEqual(actual.OrderBy(p => p.Key)))
                {
                    var changed = prior.EntitySnapshots?.FirstOrDefault(p => !actual.TryGetValue(p.Key, out var value) || p.Value != value);
                    throw new Drawing2dException("TARGET_CHANGED", $"Previously created entity {changed?.Key} changed. Expected {changed?.Value}; actual {(changed is null ? "unknown" : actual.GetValueOrDefault(changed.Value.Key))}.");
                }
                return new(true, prior.BatchId, prior.Handles, prior.Warnings, 0);
            }
        }
        Drawing2dValidator.RequireContext(plan, _context.Read(document));
        var timer = Stopwatch.StartNew();
        using var undo = new AutoCadPlanExecutor.UndoScope();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        EnsureLayers(document.Database, transaction, plan);
        var blocks = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var space = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var dimensionTextStyle = plan.Entities.Any(e => e is AlignedDimension2d or LinearDimension2d or RadialDimension2d or DiameterDimension2d)
            ? EnsureDimensionTextStyle(document.Database, transaction) : ObjectId.Null;
        var ids = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        foreach (var item in plan.Entities)
        {
            var entity = BuildEntity(document.Database, item);
            try
            {
                entity.SetDatabaseDefaults(document.Database);
                entity.Layer = item.Layer;
                if (entity is Dimension dimension)
                {
                    dimension.TextStyleId = dimensionTextStyle;
                    dimension.Dimtxt = 3.5;
                    dimension.Dimasz = 2.5;
                    dimension.Dimscale = 1;
                    dimension.Dimdec = 2;
                    dimension.Dimzin = 8;
                    var textPosition = item switch
                    {
                        RadialDimension2d radial => radial.TextPosition,
                        DiameterDimension2d diameter => diameter.TextPosition,
                        _ => null
                    };
                    if (textPosition is not null) dimension.TextPosition = P(textPosition);
                }
                ids.Add(item.Id, space.AppendEntity(entity));
                transaction.AddNewlyCreatedDBObject(entity, true);
                if (entity is Dimension dim) dim.RecomputeDimensionBlock(true);
            }
            catch { entity.Dispose(); throw; }
        }
        var handles = AutoCadPlanExecutor.ReadHandles(transaction, ids);
        var snapshots = ids.ToDictionary(p => p.Key, p => Snapshot((Entity)transaction.GetObject(p.Value, OpenMode.ForRead)), StringComparer.Ordinal);
        var batch = new BatchMetadata("2d-1.0", plan.RequestId, Guid.NewGuid().ToString("N"), OperationKind.Create,
            DateTimeOffset.UtcNow, canonical, handles, Array.Empty<string>(), snapshots);
        _metadata.Save(document.Database, transaction, batch, ids);
        transaction.Commit();
        document.Editor.Regen();
        return new(true, batch.BatchId, handles, batch.Warnings, timer.ElapsedMilliseconds);
    }

    private static void EnsureLayers(Database db, Transaction transaction, Drawing2dPlan plan)
    {
        var layers = (LayerTable)transaction.GetObject(db.LayerTableId, OpenMode.ForRead);
        var types = (LinetypeTable)transaction.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        foreach (var definition in plan.Layers)
        {
            if (layers.Has(definition.Name))
            {
                var existing = (LayerTableRecord)transaction.GetObject(layers[definition.Name], OpenMode.ForRead);
                var existingType = (LinetypeTableRecord)transaction.GetObject(existing.LinetypeObjectId, OpenMode.ForRead);
                if (existing.Color.ColorIndex != definition.Color || !existingType.Name.Equals(definition.LineType, StringComparison.OrdinalIgnoreCase))
                    throw new Drawing2dException("LAYER_CONFLICT", $"Layer {definition.Name} exists with different settings; preserve it or choose a new name.");
                continue;
            }
            if (!types.Has(definition.LineType))
            {
                try { db.LoadLineTypeFile(definition.LineType, "acadiso.lin"); }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                { throw new Drawing2dException("LINE_TYPE_MISSING", $"AutoCAD could not load {definition.LineType} from acadiso.lin."); }
            }
            layers.UpgradeOpen();
            var record = new LayerTableRecord { Name = definition.Name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, definition.Color),
                LinetypeObjectId = types[definition.LineType] };
            layers.Add(record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }
        foreach (var name in plan.Entities.Select(e => e.Layer).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!layers.Has(name)) throw new Drawing2dException("LAYER_MISSING", $"Define layer {name} before using it.");
            var layer = (LayerTableRecord)transaction.GetObject(layers[name], OpenMode.ForRead);
            if (layer.IsLocked || layer.IsOff || layer.IsFrozen)
                throw new Drawing2dException("LAYER_UNAVAILABLE", $"Layer {name} is locked, off, or frozen.");
        }
    }

    private static Entity BuildEntity(Database db, Entity2d item) => item switch
    {
        Line2d e => new Line(P(e.Start), P(e.End)),
        Circle2d e => new Circle(P(e.Center), Vector3d.ZAxis, e.Radius),
        Arc2d e => new Arc(P(e.Center), e.Radius, Radians(e.StartAngleDegrees), Radians(e.EndAngleDegrees)),
        Polyline2d e => Polyline(e),
        AlignedDimension2d e => new AlignedDimension(P(e.First), P(e.Second), P(e.LinePoint), Text(e.Prefix), db.Dimstyle),
        LinearDimension2d e => new RotatedDimension(Radians(e.RotationDegrees), P(e.First), P(e.Second), P(e.LinePoint), Text(e.Prefix), db.Dimstyle),
        RadialDimension2d e => new RadialDimension(P(e.Center), P(e.Chord), e.LeaderLength, Text(e.Prefix), db.Dimstyle),
        DiameterDimension2d e => new DiametricDimension(P(e.Chord), P(e.FarChord), e.LeaderLength, Text(e.Prefix), db.Dimstyle),
        _ => throw new Drawing2dException("UNSUPPORTED_ENTITY", "This entity type is not supported.")
    };

    private static ObjectId EnsureDimensionTextStyle(Database db, Transaction transaction)
    {
        const string name = "AICAD-2D-DIM";
        const string font = "seguisym.ttf"; // Windows font includes AutoCAD's U+2205 diameter glyph.
        var styles = (TextStyleTable)transaction.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (styles.Has(name))
        {
            var existing = (TextStyleTableRecord)transaction.GetObject(styles[name], OpenMode.ForRead);
            if (!Path.GetFileName(existing.FileName).Equals(font, StringComparison.OrdinalIgnoreCase))
                throw new Drawing2dException("TEXT_STYLE_CONFLICT", $"Text style {name} uses a different font.");
            return existing.ObjectId;
        }
        styles.UpgradeOpen();
        var record = new TextStyleTableRecord { Name = name, FileName = font };
        var id = styles.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
        return id;
    }

    private static Polyline Polyline(Polyline2d source)
    {
        var result = new Polyline(source.Vertices.Count) { Closed = source.Closed, Elevation = 0, Normal = Vector3d.ZAxis };
        for (var i = 0; i < source.Vertices.Count; i++)
        {
            var v = source.Vertices[i];
            result.AddVertexAt(i, new Point2d(v.X, v.Y), v.Bulge, 0, 0);
        }
        return result;
    }

    private static string Text(string prefix) => prefix.Length == 0 ? string.Empty : prefix + "<>";
    private static Point3d P(Point2 p) => new(p.X, p.Y, 0);
    private static double Radians(double degrees) => degrees * Math.PI / 180;

    internal static string Snapshot(Entity entity) => AutoCadPlanExecutor.Snapshot(entity)
        + FormattableString.Invariant($"|color={entity.ColorIndex}|linetype={entity.Linetype}|lts={entity.LinetypeScale:R}|weight={(int)entity.LineWeight}")
        + (entity is Polyline polyline
            ? "|widths=" + string.Join(";", Enumerable.Range(0, polyline.NumberOfVertices)
                .Select(i => FormattableString.Invariant($"{polyline.GetStartWidthAt(i):R},{polyline.GetEndWidthAt(i):R}")))
                + FormattableString.Invariant($"|normal={polyline.Normal.X:R},{polyline.Normal.Y:R},{polyline.Normal.Z:R}|thickness={polyline.Thickness:R}")
            : string.Empty)
        + (entity is Dimension dim
            ? FormattableString.Invariant($"|textPosition={Math.Round(dim.TextPosition.X, 6):R},{Math.Round(dim.TextPosition.Y, 6):R},{Math.Round(dim.TextPosition.Z, 6):R}|dimtxt={dim.Dimtxt:R}|dimasz={dim.Dimasz:R}")
            : string.Empty);

    internal static Dictionary<string, string> CaptureSnapshots(Database db, Transaction transaction, IReadOnlyDictionary<string, string> handles)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in handles)
        {
            ObjectId id;
            try { id = db.GetObjectId(false, new Handle(long.Parse(pair.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { throw new Drawing2dException("TARGET_CHANGED", "A generated entity is missing."); }
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                throw new Drawing2dException("TARGET_CHANGED", "A generated entity was erased.");
            result.Add(pair.Key, Snapshot(entity));
        }
        return result;
    }
}
