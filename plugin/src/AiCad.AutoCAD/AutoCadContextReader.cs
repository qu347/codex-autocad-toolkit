using System.Security.Cryptography;
using System.Text;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AiCad.AutoCAD;

internal sealed class AutoCadContextReader
{
    public CadDocumentContext Read(Document document)
    {
        var database = document.Database;
        var selection = ReadSelection(document);
        return new CadDocumentContext(
            ComputeFingerprint(document),
            Path.GetFileName(document.Name),
            ToLengthUnit(database.Insunits),
            document.IsReadOnly,
            document.Editor.CurrentUserCoordinateSystem.ToArray(),
            selection);
    }

    public string ComputeFingerprint(Document document)
    {
        var database = document.Database;
        var material = $"{database.FingerprintGuid}|{document.Name}|{database.Handseed}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static IReadOnlyList<CadEntitySummary> ReadSelection(Document document)
    {
        var selected = document.Editor.SelectImplied();
        if (selected.Status != PromptStatus.OK || selected.Value is null)
        {
            return Array.Empty<CadEntitySummary>();
        }

        var summaries = new List<CadEntitySummary>();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        foreach (var objectId in selected.Value.GetObjectIds())
        {
            if (transaction.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity)
            {
                continue;
            }

            try
            {
                var extents = entity.GeometricExtents;
                summaries.Add(new CadEntitySummary(
                    entity.Handle.ToString(),
                    entity.GetType().Name,
                    ToPoint(extents.MinPoint),
                    ToPoint(extents.MaxPoint)));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }

        return summaries;
    }

    private static LengthUnit? ToLengthUnit(UnitsValue unit)
    {
        return unit switch
        {
            UnitsValue.Undefined => null,
            UnitsValue.Millimeters => LengthUnit.Millimeter,
            UnitsValue.Inches => LengthUnit.Inch,
            _ => LengthUnit.DrawingUnit
        };
    }

    private static Point3 ToPoint(Autodesk.AutoCAD.Geometry.Point3d point)
    {
        return new Point3(point.X, point.Y, point.Z);
    }
}
