using AiCad.Contracts;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AiCad.AutoCAD;

internal sealed class RoundedPlate3dWriter
{
    private const double QuarterArcBulge = 0.41421356237309503;

    public ObjectId Write(
        Database database,
        Transaction transaction,
        DrawingPlan plan)
    {
        var plate = plan.Steps.OfType<RoundedPlateStep>().Single();
        var holes = plan.Steps.OfType<FourHolePatternStep>().Single();
        using var profile = CreateProfile(plan.Origin, plate);
        using var curves = new DBObjectCollection { profile };
        using var regions = Region.CreateFromCurves(curves);
        if (regions.Count != 1 || regions[0] is not Region region)
        {
            throw new InvalidOperationException("The rounded plate profile did not create one region.");
        }

        using (region)
        {
            var solid = new Solid3d();
            try
            {
                solid.SetDatabaseDefaults(database);
                solid.CreateExtrudedSolid(
                    region,
                    new Vector3d(0, 0, plate.Thickness),
                    new SweepOptions());
                SubtractHoles(solid, plan.Origin, plate, holes);

                var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);
                var objectId = modelSpace.AppendEntity(solid);
                transaction.AddNewlyCreatedDBObject(solid, true);
                return objectId;
            }
            catch
            {
                solid.Dispose();
                throw;
            }
        }
    }

    internal static Polyline CreateProfile(Point3 origin, RoundedPlateStep plate)
    {
        var radius = plate.CornerRadius;
        var width = plate.Width;
        var depth = plate.Depth;
        var profile = new Polyline(8)
        {
            Closed = true,
            Elevation = origin.Z
        };
        profile.AddVertexAt(0, new Point2d(origin.X + radius, origin.Y), 0, 0, 0);
        profile.AddVertexAt(1, new Point2d(origin.X + width - radius, origin.Y), QuarterArcBulge, 0, 0);
        profile.AddVertexAt(2, new Point2d(origin.X + width, origin.Y + radius), 0, 0, 0);
        profile.AddVertexAt(3, new Point2d(origin.X + width, origin.Y + depth - radius), QuarterArcBulge, 0, 0);
        profile.AddVertexAt(4, new Point2d(origin.X + width - radius, origin.Y + depth), 0, 0, 0);
        profile.AddVertexAt(5, new Point2d(origin.X + radius, origin.Y + depth), QuarterArcBulge, 0, 0);
        profile.AddVertexAt(6, new Point2d(origin.X, origin.Y + depth - radius), 0, 0, 0);
        profile.AddVertexAt(7, new Point2d(origin.X, origin.Y + radius), QuarterArcBulge, 0, 0);
        return profile;
    }

    private static void SubtractHoles(
        Solid3d plateSolid,
        Point3 origin,
        RoundedPlateStep plate,
        FourHolePatternStep holes)
    {
        var tolerance = Math.Max(plate.Thickness * 0.000001, 0.000001);
        var centers = new[]
        {
            (X: holes.EdgeOffsetX, Y: holes.EdgeOffsetY),
            (X: plate.Width - holes.EdgeOffsetX, Y: holes.EdgeOffsetY),
            (X: holes.EdgeOffsetX, Y: plate.Depth - holes.EdgeOffsetY),
            (X: plate.Width - holes.EdgeOffsetX, Y: plate.Depth - holes.EdgeOffsetY)
        };

        foreach (var center in centers)
        {
            using var cutter = new Solid3d();
            var radius = holes.Diameter / 2;
            cutter.CreateFrustum(plate.Thickness + (2 * tolerance), radius, radius, radius);
            cutter.TransformBy(Matrix3d.Displacement(new Vector3d(
                origin.X + center.X,
                origin.Y + center.Y,
                origin.Z + (plate.Thickness / 2))));
            plateSolid.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);
        }
    }
}
