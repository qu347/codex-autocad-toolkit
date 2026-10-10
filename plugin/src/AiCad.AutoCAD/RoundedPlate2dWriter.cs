using AiCad.Contracts;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AiCad.AutoCAD;

internal sealed class RoundedPlate2dWriter
{
    internal const string OutlineLayer = "AICAD-2D-OUTLINE";
    internal const string CenterLayer = "AICAD-2D-CENTER";
    internal const string DimensionLayer = "AICAD-2D-DIM";

    public IReadOnlyDictionary<string, ObjectId> Write(
        Database database,
        Transaction transaction,
        DrawingPlan plan)
    {
        var plate = plan.Steps.OfType<RoundedPlateStep>().Single();
        var holes = plan.Steps.OfType<FourHolePatternStep>().Single();
        EnsureLayers(database, transaction);

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForWrite);
        var entities = new Dictionary<string, ObjectId>(StringComparer.Ordinal);

        var topOrigin = new Point3(
            plan.Origin.X + plate.Width + plate.Depth + 60,
            plan.Origin.Y,
            0);
        var frontOrigin = new Point3(
            topOrigin.X,
            topOrigin.Y - plate.Thickness - 30,
            0);
        var leftOrigin = new Point3(
            topOrigin.X - plate.Depth - 30,
            topOrigin.Y,
            0);

        var topOutline = RoundedPlate3dWriter.CreateProfile(topOrigin, plate);
        topOutline.Layer = OutlineLayer;
        AddEntity(database, transaction, modelSpace, entities, "top-outline", topOutline);

        var holeCenters = GetHoleCenters(topOrigin, plate, holes);
        for (var index = 0; index < holeCenters.Length; index++)
        {
            var semanticNumber = index + 1;
            var center = holeCenters[index];
            AddEntity(
                database,
                transaction,
                modelSpace,
                entities,
                $"top-hole-{semanticNumber}",
                new Circle(center, Vector3d.ZAxis, holes.Diameter / 2) { Layer = OutlineLayer });
            AddCenterMark(
                database,
                transaction,
                modelSpace,
                entities,
                semanticNumber,
                center,
                (holes.Diameter / 2) + 2);
        }

        AddEntity(
            database,
            transaction,
            modelSpace,
            entities,
            "front-outline",
            CreateRectangle(frontOrigin, plate.Width, plate.Thickness));
        AddEntity(
            database,
            transaction,
            modelSpace,
            entities,
            "left-outline",
            CreateRectangle(leftOrigin, plate.Depth, plate.Thickness));

        if (plan.Output.AddDimensions)
        {
            AddDimensions(database, transaction, modelSpace, entities, topOrigin, frontOrigin, plate, holes, holeCenters[0]);
        }

        return entities;
    }

    private static void EnsureLayers(Database database, Transaction transaction)
    {
        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var linetypes = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        EnsureLayer(database, transaction, layers, OutlineLayer, database.ContinuousLinetype);

        var centerLinetype = linetypes.Has("CENTER2")
            ? linetypes["CENTER2"]
            : linetypes.Has("CENTER")
                ? linetypes["CENTER"]
                : database.ContinuousLinetype;
        EnsureLayer(database, transaction, layers, CenterLayer, centerLinetype);
        EnsureLayer(database, transaction, layers, DimensionLayer, database.ContinuousLinetype);
    }

    private static void EnsureLayer(
        Database database,
        Transaction transaction,
        LayerTable layers,
        string name,
        ObjectId linetypeId)
    {
        if (layers.Has(name))
        {
            return;
        }

        if (!layers.IsWriteEnabled)
        {
            layers.UpgradeOpen();
        }

        var layer = new LayerTableRecord
        {
            Name = name,
            LinetypeObjectId = linetypeId
        };
        layers.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
    }

    private static Point3d[] GetHoleCenters(
        Point3 topOrigin,
        RoundedPlateStep plate,
        FourHolePatternStep holes)
    {
        return new[]
        {
            new Point3d(topOrigin.X + holes.EdgeOffsetX, topOrigin.Y + holes.EdgeOffsetY, 0),
            new Point3d(topOrigin.X + plate.Width - holes.EdgeOffsetX, topOrigin.Y + holes.EdgeOffsetY, 0),
            new Point3d(topOrigin.X + holes.EdgeOffsetX, topOrigin.Y + plate.Depth - holes.EdgeOffsetY, 0),
            new Point3d(topOrigin.X + plate.Width - holes.EdgeOffsetX, topOrigin.Y + plate.Depth - holes.EdgeOffsetY, 0)
        };
    }

    private static void AddCenterMark(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        IDictionary<string, ObjectId> entities,
        int semanticNumber,
        Point3d center,
        double extent)
    {
        AddEntity(
            database,
            transaction,
            modelSpace,
            entities,
            $"top-hole-{semanticNumber}-center-x",
            new Line(
                new Point3d(center.X - extent, center.Y, 0),
                new Point3d(center.X + extent, center.Y, 0)) { Layer = CenterLayer });
        AddEntity(
            database,
            transaction,
            modelSpace,
            entities,
            $"top-hole-{semanticNumber}-center-y",
            new Line(
                new Point3d(center.X, center.Y - extent, 0),
                new Point3d(center.X, center.Y + extent, 0)) { Layer = CenterLayer });
    }

    private static Polyline CreateRectangle(Point3 origin, double width, double height)
    {
        var rectangle = new Polyline(4)
        {
            Closed = true,
            Elevation = 0,
            Layer = OutlineLayer
        };
        rectangle.AddVertexAt(0, new Point2d(origin.X, origin.Y), 0, 0, 0);
        rectangle.AddVertexAt(1, new Point2d(origin.X + width, origin.Y), 0, 0, 0);
        rectangle.AddVertexAt(2, new Point2d(origin.X + width, origin.Y + height), 0, 0, 0);
        rectangle.AddVertexAt(3, new Point2d(origin.X, origin.Y + height), 0, 0, 0);
        return rectangle;
    }

    private static void AddDimensions(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        IDictionary<string, ObjectId> entities,
        Point3 topOrigin,
        Point3 frontOrigin,
        RoundedPlateStep plate,
        FourHolePatternStep holes,
        Point3d firstHole)
    {
        var topLowerLeft = new Point3d(topOrigin.X, topOrigin.Y, 0);
        AddAlignedDimension(
            database,
            transaction,
            modelSpace,
            entities,
            "dim-width",
            topLowerLeft,
            new Point3d(topOrigin.X + plate.Width, topOrigin.Y, 0),
            new Point3d(topOrigin.X + (plate.Width / 2), topOrigin.Y + plate.Depth + 15, 0));
        AddAlignedDimension(
            database,
            transaction,
            modelSpace,
            entities,
            "dim-depth",
            topLowerLeft,
            new Point3d(topOrigin.X, topOrigin.Y + plate.Depth, 0),
            new Point3d(topOrigin.X - 15, topOrigin.Y + (plate.Depth / 2), 0));
        AddAlignedDimension(
            database,
            transaction,
            modelSpace,
            entities,
            "dim-thickness",
            new Point3d(frontOrigin.X + plate.Width, frontOrigin.Y, 0),
            new Point3d(frontOrigin.X + plate.Width, frontOrigin.Y + plate.Thickness, 0),
            new Point3d(frontOrigin.X + plate.Width + 15, frontOrigin.Y + (plate.Thickness / 2), 0));
        AddAlignedDimension(
            database,
            transaction,
            modelSpace,
            entities,
            "dim-edge-x",
            topLowerLeft,
            new Point3d(firstHole.X, topOrigin.Y, 0),
            new Point3d(topOrigin.X + (holes.EdgeOffsetX / 2), topOrigin.Y - 10, 0));
        AddAlignedDimension(
            database,
            transaction,
            modelSpace,
            entities,
            "dim-edge-y",
            topLowerLeft,
            new Point3d(topOrigin.X, firstHole.Y, 0),
            new Point3d(topOrigin.X - 10, topOrigin.Y + (holes.EdgeOffsetY / 2), 0));

        var radius = holes.Diameter / 2;
        var diameter = new DiametricDimension(
            new Point3d(firstHole.X + radius, firstHole.Y, 0),
            new Point3d(firstHole.X - radius, firstHole.Y, 0),
            10,
            "4X <>",
            database.Dimstyle)
        {
            Layer = DimensionLayer
        };
        AddEntity(database, transaction, modelSpace, entities, "dim-hole-diameter", diameter);
    }

    private static void AddAlignedDimension(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        IDictionary<string, ObjectId> entities,
        string semanticId,
        Point3d first,
        Point3d second,
        Point3d dimensionLine)
    {
        var dimension = new AlignedDimension(
            first,
            second,
            dimensionLine,
            string.Empty,
            database.Dimstyle)
        {
            Layer = DimensionLayer
        };
        AddEntity(database, transaction, modelSpace, entities, semanticId, dimension);
    }

    private static void AddEntity(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        IDictionary<string, ObjectId> entities,
        string semanticId,
        Entity entity)
    {
        try
        {
            entity.SetDatabaseDefaults(database);
            var objectId = modelSpace.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
            entities.Add(semanticId, objectId);
        }
        catch
        {
            entity.Dispose();
            throw;
        }
    }
}
