using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AiCad.Mechanical;
using AiCad.Contracts;
using System.Globalization;
using System.Text.Json;

[assembly: CommandClass(typeof(AiCad.AutoCAD.Commands))]

namespace AiCad.AutoCAD;

public sealed class Commands
{
    private static readonly Guid PlateTestRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NativeUndoTestRequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [CommandMethod("AICADSTATUS", CommandFlags.Session)]
    public void ShowStatus()
    {
        var status = PluginEntry.Current?.GetStatusLine() ?? "AICAD_NOT_READY";
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n{status}");
    }

    [CommandMethod("AICADWAITFORRPC", CommandFlags.Session)]
    public void WaitForRpc()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (PluginEntry.Current?.ProcessPendingRequests() > 0)
            {
                return;
            }

            Thread.Sleep(10);
        }

        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nAICAD_RPC_WAIT_TIMEOUT");
    }

    [CommandMethod("AICADTESTCREATEPLATE")]
    public void CreateTestPlate()
    {
        try
        {
            CreateTestPlateCore();
        }
        catch (System.Exception exception)
        {
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nAICAD_TEST_FAIL create_plate {exception}");
        }
    }

    private static void CreateTestPlateCore()
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active drawing.");
        var contextReader = new AutoCadContextReader();
        var plan = new RoundedPlatePlanFactory().Create(
            new RoundedPlateRequest(100, 60, 10, 5, 8, 10, 10, true, true, true),
            contextReader.ComputeFingerprint(document),
            PlateTestRequestId,
            LengthUnit.Millimeter);
        var result = new AutoCadPlanExecutor().ExecuteCreate(document, plan);
        document.Editor.WriteMessage($"\nAICAD_TEST_CREATED batch={result.BatchId}");
    }

    [CommandMethod("AICADTESTVERIFYPLATE")]
    public void VerifyTestPlate()
    {
        try
        {
            VerifyTestPlateCore();
        }
        catch (System.Exception exception)
        {
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nAICAD_TEST_FAIL plate3d {exception}");
        }
    }

    private static void VerifyTestPlateCore()
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active drawing.");
        var database = document.Database;
        BatchMetadata metadata;
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            metadata = new BatchMetadataStore().FindByRequest(database, transaction, PlateTestRequestId)
                ?? throw new InvalidOperationException("Plate test metadata was not found.");
            Require(metadata.Handles.ContainsKey("plate-solid"), "The batch has no plate solid.");
            var handleText = metadata.Handles["plate-solid"];
            var handle = new Handle(long.Parse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var objectId = database.GetObjectId(false, handle, 0);
            var solid = transaction.GetObject(objectId, OpenMode.ForRead) as Solid3d
                ?? throw new InvalidOperationException("The plate handle is not a Solid3d.");
            var extents = solid.GeometricExtents;
            Require(Near(extents.MinPoint.X, 0) && Near(extents.MaxPoint.X, 100), "Unexpected X extents.");
            Require(Near(extents.MinPoint.Y, 0) && Near(extents.MaxPoint.Y, 60), "Unexpected Y extents.");
            Require(Near(extents.MinPoint.Z, 0) && Near(extents.MaxPoint.Z, 10), "Unexpected Z extents.");

            var roundedArea = (100 * 60) - ((4 - Math.PI) * 5 * 5);
            var expectedVolume = (roundedArea * 10) - (4 * Math.PI * 4 * 4 * 10);
            var actualVolume = solid.MassProperties.Volume;
            var relativeError = Math.Abs(actualVolume - expectedVolume) / expectedVolume;
            Require(
                relativeError < 0.000001,
                $"Unexpected volume error {relativeError}; actual={actualVolume}, expected={expectedVolume}.");

            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            Require(layerTable.Has("AICAD-2D-OUTLINE"), "The 2D outline layer was not created.");
            Require(layerTable.Has("AICAD-2D-CENTER"), "The 2D center layer was not created.");
            Require(layerTable.Has("AICAD-2D-DIM"), "The 2D dimension layer was not created.");

            var required2dIds = new[]
            {
                "top-outline",
                "top-hole-1",
                "top-hole-2",
                "top-hole-3",
                "top-hole-4",
                "front-outline",
                "left-outline",
                "dim-width",
                "dim-depth",
                "dim-thickness",
                "dim-hole-diameter",
                "dim-edge-x",
                "dim-edge-y"
            };
            foreach (var semanticId in required2dIds)
            {
                Require(metadata.Handles.ContainsKey(semanticId), $"Missing 2D entity {semanticId}.");
            }

            var topOutline = OpenEntity<Polyline>(database, transaction, metadata, "top-outline");
            Require(topOutline.Closed, "The top outline is not closed.");
            for (var index = 1; index <= 4; index++)
            {
                OpenEntity<Circle>(database, transaction, metadata, $"top-hole-{index}");
            }

            Require(OpenEntity<Polyline>(database, transaction, metadata, "front-outline").Closed, "The front outline is not closed.");
            Require(OpenEntity<Polyline>(database, transaction, metadata, "left-outline").Closed, "The left outline is not closed.");

            var dimensionCount = 0;
            foreach (var pair in metadata.Handles.Where(pair => pair.Key.StartsWith("dim-", StringComparison.Ordinal)))
            {
                OpenEntity<Dimension>(database, transaction, metadata, pair.Key);
                dimensionCount++;
            }

            Require(dimensionCount >= 6, "Fewer than six base dimensions were created.");
            foreach (var pair in metadata.Handles.Where(pair => pair.Key != "plate-solid"))
            {
                var entity = OpenEntity<Entity>(database, transaction, metadata, pair.Key);
                Require(!entity.ExtensionDictionary.IsNull, $"2D entity {pair.Key} has no semantic metadata.");
                Require(entity.GeometricExtents.MinPoint.X > extents.MaxPoint.X, $"2D entity {pair.Key} overlaps the 3D part area.");
            }
        }

        var originalPlan = JsonSerializer.Deserialize<DrawingPlan>(metadata.PlanJson, PlanJson.Options)
            ?? throw new InvalidOperationException("Stored plate plan could not be read.");
        var duplicate = new AutoCadPlanExecutor().ExecuteCreate(document, originalPlan);
        Require(duplicate.BatchId == metadata.BatchId, "Duplicate request created a new batch.");
        Require(duplicate.Handles["plate-solid"] == metadata.Handles["plate-solid"], "Duplicate request changed the entity handle.");
        document.Editor.WriteMessage("\nAICAD_TEST_PASS plate2d3d");
        document.Editor.WriteMessage("\nAICAD_TEST_PASS duplicate_request");
    }

    [CommandMethod("AICADTESTAPPROVAL")]
    public void VerifyApprovalFlow()
    {
        try
        {
            var document = Application.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("No active drawing.");
            var store = new BatchMetadataStore();
            BatchMetadata created;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                created = store.FindByRequest(document.Database, transaction, PlateTestRequestId)
                    ?? throw new InvalidOperationException("Created plate batch was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var expiringExecutor = new AutoCadPlanExecutor(new ApprovalStore(() => now));
            var expiringProposal = expiringExecutor.ProposeHoleDiameterChange(document, created.BatchId, 10);
            now = now.AddMinutes(6);
            try
            {
                expiringExecutor.ApplyChange(document, expiringProposal.ChangeId, expiringProposal.ApprovalToken);
                throw new InvalidOperationException("An expired approval token was accepted.");
            }
            catch (CadOperationException exception) when (exception.Code == "APPROVAL_EXPIRED")
            {
            }

            Require(Near(ReadHoleDiameter(document, created.BatchId), 8), "Expired proposal changed the drawing.");

            var executor = new AutoCadPlanExecutor();
            var cancelledProposal = executor.ProposeHoleDiameterChange(document, created.BatchId, 10);
            Require(Near(ReadHoleDiameter(document, created.BatchId), 8), "Proposal changed the drawing before approval.");
            try
            {
                executor.CancelChange(cancelledProposal.ChangeId, "wrong-token");
                throw new InvalidOperationException("An invalid approval token cancelled the proposal.");
            }
            catch (CadOperationException exception) when (exception.Code == "APPROVAL_REQUIRED")
            {
            }

            var cancelled = executor.CancelChange(cancelledProposal.ChangeId, cancelledProposal.ApprovalToken);
            Require(cancelled.Cancelled, "The pending proposal was not cancelled.");
            try
            {
                executor.ApplyChange(document, cancelledProposal.ChangeId, cancelledProposal.ApprovalToken);
                throw new InvalidOperationException("A cancelled approval token was accepted.");
            }
            catch (CadOperationException exception) when (exception.Code == "APPROVAL_REQUIRED")
            {
            }

            Require(Near(ReadHoleDiameter(document, created.BatchId), 8), "Cancelled proposal changed the drawing.");

            var changedTargetExecutor = new AutoCadPlanExecutor();
            var changedTargetProposal = changedTargetExecutor.ProposeHoleDiameterChange(document, created.BatchId, 10);
            SetCircleRadius(document, created.Handles["top-hole-1"], 5);
            try
            {
                changedTargetExecutor.ApplyChange(
                    document,
                    changedTargetProposal.ChangeId,
                    changedTargetProposal.ApprovalToken);
                throw new InvalidOperationException("A proposal was applied after its target changed.");
            }
            catch (CadOperationException exception) when (exception.Code == "TARGET_CHANGED")
            {
            }
            finally
            {
                SetCircleRadius(document, created.Handles["top-hole-1"], 4);
            }

            document.Editor.WriteMessage("\nAICAD_TEST_PASS approval_cancel");

            var approvedProposal = executor.ProposeHoleDiameterChange(document, created.BatchId, 10);
            var modified = executor.ApplyChange(document, approvedProposal.ChangeId, approvedProposal.ApprovalToken);
            Require(Near(ReadHoleDiameter(document, modified.BatchId), 10), "Approved proposal did not change the hole diameter.");
            document.Editor.WriteMessage("\nAICAD_TEST_PASS approval_apply");

            var undone = executor.UndoBatch(document, modified.BatchId);
            Require(undone.RestoredResult is not null, "Undo did not restore the previous plate version.");
            Require(Near(ReadHoleDiameter(document, undone.RestoredResult!.BatchId), 8), "Batch undo did not restore diameter 8.");
            document.Editor.WriteMessage("\nAICAD_TEST_PASS batch_undo");
        }
        catch (System.Exception exception)
        {
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nAICAD_TEST_FAIL approval {exception}");
        }
    }

    [CommandMethod("AICADTESTCREATENATIVE")]
    public void CreateNativeUndoBatch()
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active drawing.");
        var plan = new RoundedPlatePlanFactory().Create(
            new RoundedPlateRequest(40, 30, 5, 3, 4, 7, 7, true, true, true),
            new AutoCadContextReader().ComputeFingerprint(document),
            NativeUndoTestRequestId,
            LengthUnit.Millimeter,
            new Point3(0, 100, 0));
        new AutoCadPlanExecutor().ExecuteCreate(document, plan);
    }

    [CommandMethod("AICADTESTVERIFYNATIVEUNDO")]
    public void VerifyNativeUndoBatch()
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("No active drawing.");
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadata = new BatchMetadataStore().FindByRequest(
            document.Database,
            transaction,
            NativeUndoTestRequestId);
        if (metadata is null)
        {
            document.Editor.WriteMessage("\nAICAD_TEST_PASS native_undo_group");
            return;
        }

        document.Editor.WriteMessage("\nAICAD_TEST_FAIL native_undo_group metadata_still_exists");
    }

    private static double ReadHoleDiameter(
        Autodesk.AutoCAD.ApplicationServices.Document document,
        string batchId)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadata = new BatchMetadataStore().FindByBatch(document.Database, transaction, batchId)
            ?? throw new InvalidOperationException($"Batch {batchId} was not found.");
        var plan = JsonSerializer.Deserialize<DrawingPlan>(metadata.PlanJson, PlanJson.Options)
            ?? throw new InvalidOperationException($"Batch {batchId} has no readable plan.");
        return plan.Steps.OfType<FourHolePatternStep>().Single().Diameter;
    }

    private static void SetCircleRadius(
        Autodesk.AutoCAD.ApplicationServices.Document document,
        string handleText,
        double radius)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var handle = new Handle(long.Parse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var objectId = document.Database.GetObjectId(false, handle, 0);
        var circle = transaction.GetObject(objectId, OpenMode.ForWrite) as Circle
            ?? throw new InvalidOperationException("The target test entity is not a circle.");
        circle.Radius = radius;
        transaction.Commit();
    }

    private static T OpenEntity<T>(
        Database database,
        Transaction transaction,
        BatchMetadata metadata,
        string semanticId)
        where T : Entity
    {
        var handle = new Handle(long.Parse(
            metadata.Handles[semanticId],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture));
        var objectId = database.GetObjectId(false, handle, 0);
        return transaction.GetObject(objectId, OpenMode.ForRead) as T
            ?? throw new InvalidOperationException($"Entity {semanticId} is not a {typeof(T).Name}.");
    }

    private static bool Near(double actual, double expected)
    {
        return Math.Abs(actual - expected) <= 0.000001;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
