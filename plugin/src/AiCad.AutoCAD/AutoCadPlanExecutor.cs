using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AiCad.Contracts;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AiCad.AutoCAD;

internal sealed class AutoCadPlanExecutor
{
    private readonly AutoCadContextReader _contextReader = new();
    private readonly RoundedPlate3dWriter _plate3dWriter = new();
    private readonly RoundedPlate2dWriter _plate2dWriter = new();
    private readonly BatchMetadataStore _metadataStore = new();
    private readonly ApprovalStore _approvalStore;

    public AutoCadPlanExecutor()
        : this(new ApprovalStore())
    {
    }

    internal AutoCadPlanExecutor(ApprovalStore approvalStore)
    {
        _approvalStore = approvalStore;
    }

    public CreateResult ExecuteCreate(Document document, DrawingPlan plan)
    {
        var validation = PlanValidator.Validate(plan);
        if (!validation.IsValid)
        {
            throw new PlanValidationException(validation.Errors);
        }

        if (plan.DeclaredOperation != OperationKind.Create || OperationPolicy.RequiresApproval(plan))
        {
            throw new InvalidOperationException("Only a validated create plan may use the direct create path.");
        }

        using var documentLock = document.LockDocument();
        using (var lookup = document.Database.TransactionManager.StartTransaction())
        {
            var existing = _metadataStore.FindByRequest(document.Database, lookup, plan.RequestId);
            if (existing is not null)
            {
                return ToCreateResult(existing, 0);
            }
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException("The drawing is read-only.");
        }

        if (!string.Equals(
                plan.DocumentFingerprint,
                _contextReader.ComputeFingerprint(document),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The drawing changed after the plan was created.");
        }

        var stopwatch = Stopwatch.StartNew();
        var batchId = Guid.NewGuid().ToString("N");
        using var undo = new UndoScope();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var entities = WritePlanEntities(document.Database, transaction, plan);
        var handles = ReadHandles(transaction, entities);
        var snapshots = CaptureSnapshots(transaction, entities);

        var metadata = new BatchMetadata(
            plan.SchemaVersion,
            plan.RequestId,
            batchId,
            OperationKind.Create,
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(plan, PlanJson.Options),
            handles,
            Array.Empty<string>(),
            snapshots);
        _metadataStore.Save(document.Database, transaction, metadata, entities);
        transaction.Commit();
        stopwatch.Stop();
        document.Editor.Regen();
        return ToCreateResult(metadata, stopwatch.ElapsedMilliseconds);
    }

    public RequestStatusResult GetRequestStatus(Document document, Guid requestId)
    {
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadata = _metadataStore.FindByRequest(document.Database, transaction, requestId);
        if (metadata is { SchemaVersion: "2d-1.0", IsUndone: true })
            return new RequestStatusResult(requestId, "undone", null);
        return metadata is null
            ? new RequestStatusResult(requestId, "not_found", null)
            : new RequestStatusResult(requestId, "completed", ToCreateResult(metadata, 0));
    }

    public ProposedChangeResult ProposeHoleDiameterChange(
        Document document,
        string batchId,
        double newHoleDiameter)
    {
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadata = RequireActiveBatch(document.Database, transaction, batchId);
        if (metadata.SchemaVersion != "1.0")
            throw new CadOperationException("UNSUPPORTED_BATCH", "Hole-diameter replacement applies only to rounded-plate batches. For a 2D stage, inspect and undo the verified latest batch before rebuilding.");
        var plan = ReadPlan(metadata.PlanJson);
        var oldHoles = plan.Steps.OfType<FourHolePatternStep>().Single();
        var proposedPlan = ReplaceHoleDiameter(
            plan,
            newHoleDiameter,
            plan.RequestId,
            _contextReader.ComputeFingerprint(document));
        RequireValid(proposedPlan);

        var currentSnapshots = CaptureSnapshots(document.Database, transaction, metadata.Handles);
        RequireSnapshotsMatch(metadata.EntitySnapshots, currentSnapshots);
        return _approvalStore.Create(
            _contextReader.ComputeFingerprint(document),
            metadata.BatchId,
            oldHoles.Diameter,
            newHoleDiameter,
            currentSnapshots);
    }

    public CancelChangeResult CancelChange(Guid changeId, string approvalToken)
    {
        return _approvalStore.Cancel(changeId, approvalToken);
    }

    public CreateResult ApplyChange(Document document, Guid changeId, string approvalToken)
    {
        var pending = _approvalStore.Consume(changeId, approvalToken);
        using var documentLock = document.LockDocument();
        if (document.IsReadOnly)
        {
            throw new CadOperationException("READ_ONLY_DOCUMENT", "The drawing is read-only.");
        }

        var currentFingerprint = _contextReader.ComputeFingerprint(document);
        if (!string.Equals(pending.DocumentFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            throw new CadOperationException("DOCUMENT_CHANGED", "The drawing changed after approval was proposed.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var undo = new UndoScope();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var previous = RequireActiveBatch(document.Database, transaction, pending.BatchId);
        var currentSnapshots = CaptureSnapshots(document.Database, transaction, previous.Handles);
        RequireSnapshotsMatch(pending.TargetHandleSnapshots, currentSnapshots);

        var previousPlan = ReadPlan(previous.PlanJson);
        var modifiedPlan = ReplaceHoleDiameter(
            previousPlan,
            pending.NewHoleDiameter,
            changeId,
            currentFingerprint);
        RequireValid(modifiedPlan);
        EraseEntities(document.Database, transaction, previous.Handles);
        var entities = WritePlanEntities(document.Database, transaction, modifiedPlan);
        var handles = ReadHandles(transaction, entities);
        var snapshots = CaptureSnapshots(transaction, entities);
        var metadata = new BatchMetadata(
            modifiedPlan.SchemaVersion,
            changeId,
            Guid.NewGuid().ToString("N"),
            OperationKind.Modify,
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(modifiedPlan, PlanJson.Options),
            handles,
            Array.Empty<string>(),
            snapshots,
            previous.BatchId,
            previous.PlanJson);
        _metadataStore.MarkUndone(document.Database, transaction, previous);
        _metadataStore.Save(document.Database, transaction, metadata, entities);
        transaction.Commit();
        stopwatch.Stop();
        document.Editor.Regen();
        return ToCreateResult(metadata, stopwatch.ElapsedMilliseconds);
    }

    public UndoBatchResult UndoBatch(Document document, string batchId)
    {
        using var documentLock = document.LockDocument();
        if (document.IsReadOnly)
        {
            throw new CadOperationException("READ_ONLY_DOCUMENT", "The drawing is read-only.");
        }

        var currentFingerprint = _contextReader.ComputeFingerprint(document);
        using var undo = new UndoScope();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var latest = _metadataStore.FindLatestBatchId(document.Database, transaction);
        if (!string.Equals(latest, batchId, StringComparison.Ordinal))
        {
            throw new CadOperationException("TARGET_CHANGED", "Only the most recent active AI-CAD batch can be undone.");
        }

        var target = RequireActiveBatch(document.Database, transaction, batchId);
        var currentSnapshots = target.SchemaVersion == "2d-1.0"
            ? Drawing2dExecutor.CaptureSnapshots(document.Database, transaction, target.Handles)
            : CaptureSnapshots(document.Database, transaction, target.Handles);
        RequireSnapshotsMatch(target.EntitySnapshots, currentSnapshots);
        EraseEntities(document.Database, transaction, target.Handles);

        var undoRequestId = Guid.NewGuid();
        var undoBatchId = Guid.NewGuid().ToString("N");
        CreateResult? restoredResult = null;
        BatchMetadata undoMetadata;
        IReadOnlyDictionary<string, ObjectId> restoredEntities;
        if (target.Operation == OperationKind.Create)
        {
            restoredEntities = new Dictionary<string, ObjectId>();
            undoMetadata = new BatchMetadata(
                "1.0",
                undoRequestId,
                undoBatchId,
                OperationKind.Delete,
                DateTimeOffset.UtcNow,
                target.PlanJson,
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                new Dictionary<string, string>(),
                target.BatchId,
                target.PlanJson);
        }
        else if (target.Operation == OperationKind.Modify && target.PreviousPlanJson is not null)
        {
            var restoredPlan = ReadPlan(target.PreviousPlanJson) with
            {
                RequestId = undoRequestId,
                DocumentFingerprint = currentFingerprint
            };
            RequireValid(restoredPlan);
            restoredEntities = WritePlanEntities(document.Database, transaction, restoredPlan);
            var restoredHandles = ReadHandles(transaction, restoredEntities);
            var restoredSnapshots = CaptureSnapshots(transaction, restoredEntities);
            undoMetadata = new BatchMetadata(
                restoredPlan.SchemaVersion,
                undoRequestId,
                undoBatchId,
                OperationKind.Modify,
                DateTimeOffset.UtcNow,
                JsonSerializer.Serialize(restoredPlan, PlanJson.Options),
                restoredHandles,
                Array.Empty<string>(),
                restoredSnapshots,
                target.BatchId,
                target.PlanJson);
            restoredResult = ToCreateResult(undoMetadata, 0);
        }
        else
        {
            throw new CadOperationException("INVALID_PLAN", "This AI-CAD batch cannot be reconstructed for undo.");
        }

        _metadataStore.MarkUndone(document.Database, transaction, target);
        _metadataStore.Save(document.Database, transaction, undoMetadata, restoredEntities);
        transaction.Commit();
        document.Editor.Regen();
        return new UndoBatchResult(true, undoBatchId, target.BatchId, restoredResult);
    }

    private Dictionary<string, ObjectId> WritePlanEntities(
        Database database,
        Transaction transaction,
        DrawingPlan plan)
    {
        var entities = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        if (plan.Output.Create3D)
        {
            entities.Add("plate-solid", _plate3dWriter.Write(database, transaction, plan));
        }

        if (plan.Output.Create2D)
        {
            foreach (var pair in _plate2dWriter.Write(database, transaction, plan))
            {
                entities.Add(pair.Key, pair.Value);
            }
        }

        return entities;
    }

    internal static Dictionary<string, string> ReadHandles(
        Transaction transaction,
        IReadOnlyDictionary<string, ObjectId> entities)
    {
        return entities.ToDictionary(
            pair => pair.Key,
            pair => ((Entity)transaction.GetObject(pair.Value, OpenMode.ForRead)).Handle.ToString(),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, string> CaptureSnapshots(
        Transaction transaction,
        IReadOnlyDictionary<string, ObjectId> entities)
    {
        return entities.ToDictionary(
            pair => pair.Key,
            pair => Snapshot((Entity)transaction.GetObject(pair.Value, OpenMode.ForRead)),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, string> CaptureSnapshots(
        Database database,
        Transaction transaction,
        IReadOnlyDictionary<string, string> handles)
    {
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in handles)
        {
            snapshots.Add(pair.Key, Snapshot(OpenEntity(database, transaction, pair.Value, OpenMode.ForRead)));
        }

        return snapshots;
    }

    internal static string Snapshot(Entity entity)
    {
        var prefix = $"{entity.Handle}|{entity.GetType().Name}|{entity.Layer}";
        return entity switch
        {
            Solid3d solid => SnapshotSolid(prefix, solid),
            Polyline polyline => SnapshotPolyline(prefix, polyline),
            Circle circle => $"{prefix}|center={Point(circle.Center)}|radius={Number(circle.Radius)}",
            Arc arc => $"{prefix}|center={Point(arc.Center)}|radius={Number(arc.Radius)}|start={Number(arc.StartAngle)}|end={Number(arc.EndAngle)}",
            RadialDimension dimension => $"{prefix}|center={Point(dimension.Center)}|chord={Point(dimension.ChordPoint)}|leader={Number(dimension.LeaderLength)}|text={dimension.DimensionText}|position={Point(dimension.TextPosition)}",
            RotatedDimension dimension => $"{prefix}|p1={Point(dimension.XLine1Point)}|p2={Point(dimension.XLine2Point)}|line={Point(dimension.DimLinePoint)}|rotation={Number(dimension.Rotation)}|text={dimension.DimensionText}",
            Line line => FormattableString.Invariant(
                $"{prefix}|start={Point(line.StartPoint)}|end={Point(line.EndPoint)}"),
            AlignedDimension dimension =>
                $"{prefix}|p1={Point(dimension.XLine1Point)}|p2={Point(dimension.XLine2Point)}|offset={Number(AlignedOffset(dimension))}|text={dimension.DimensionText}",
            DiametricDimension dimension =>
                $"{prefix}|chord={Point(dimension.ChordPoint)}|far={Point(dimension.FarChordPoint)}|text={dimension.DimensionText}",
            _ => SnapshotExtents(prefix, entity)
        };
    }

    private static string SnapshotSolid(string prefix, Solid3d solid)
    {
        return $"{SnapshotExtents(prefix, solid)}|volume={Number(solid.MassProperties.Volume)}";
    }

    private static string SnapshotPolyline(string prefix, Polyline polyline)
    {
        var vertices = Enumerable.Range(0, polyline.NumberOfVertices)
            .Select(index =>
                $"{Number(polyline.GetPoint2dAt(index).X)},{Number(polyline.GetPoint2dAt(index).Y)},{Number(polyline.GetBulgeAt(index))}");
        return $"{prefix}|closed={polyline.Closed}|elevation={Number(polyline.Elevation)}|vertices={string.Join(";", vertices)}";
    }

    private static string SnapshotExtents(string prefix, Entity entity)
    {
        var extents = entity.GeometricExtents;
        return FormattableString.Invariant(
            $"{prefix}|min={Point(extents.MinPoint)}|max={Point(extents.MaxPoint)}");
    }

    private static string Point(Autodesk.AutoCAD.Geometry.Point3d point)
    {
        return $"{Number(point.X)},{Number(point.Y)},{Number(point.Z)}";
    }

    private static string Number(double value)
    {
        return Math.Round(value, 9).ToString("R", CultureInfo.InvariantCulture);
    }

    private static double AlignedOffset(AlignedDimension dimension)
    {
        var first = dimension.XLine1Point;
        var second = dimension.XLine2Point;
        var line = dimension.DimLinePoint;
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        return ((deltaX * (line.Y - first.Y)) - (deltaY * (line.X - first.X))) / length;
    }

    private static void EraseEntities(
        Database database,
        Transaction transaction,
        IReadOnlyDictionary<string, string> handles)
    {
        foreach (var handle in handles.Values)
        {
            OpenEntity(database, transaction, handle, OpenMode.ForWrite).Erase();
        }
    }

    private static Entity OpenEntity(
        Database database,
        Transaction transaction,
        string handleText,
        OpenMode mode)
    {
        try
        {
            var handle = new Handle(long.Parse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var objectId = database.GetObjectId(false, handle, 0);
            if (objectId.IsErased)
            {
                throw new CadOperationException("TARGET_CHANGED", $"AI-CAD entity {handleText} was erased.");
            }

            return transaction.GetObject(objectId, mode, false) as Entity
                ?? throw new CadOperationException("TARGET_CHANGED", $"AI-CAD object {handleText} is no longer an entity.");
        }
        catch (CadOperationException)
        {
            throw;
        }
        catch (System.Exception exception) when (exception is FormatException or Autodesk.AutoCAD.Runtime.Exception)
        {
            throw new CadOperationException("TARGET_CHANGED", $"AI-CAD entity {handleText} is unavailable.");
        }
    }

    private BatchMetadata RequireActiveBatch(
        Database database,
        Transaction transaction,
        string batchId)
    {
        var metadata = _metadataStore.FindByBatch(database, transaction, batchId)
            ?? throw new CadOperationException("TARGET_CHANGED", $"AI-CAD batch {batchId} was not found.");
        if (metadata.IsUndone)
        {
            throw new CadOperationException("TARGET_CHANGED", $"AI-CAD batch {batchId} is no longer active.");
        }

        return metadata;
    }

    private static void RequireSnapshotsMatch(
        IReadOnlyDictionary<string, string>? expected,
        IReadOnlyDictionary<string, string> actual)
    {
        if (expected is null || expected.Count != actual.Count)
        {
            throw new CadOperationException("TARGET_CHANGED", "One or more AI-CAD target entities changed.");
        }

        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                throw new CadOperationException(
                    "TARGET_CHANGED",
                    $"AI-CAD target {pair.Key} changed. Expected '{pair.Value}', actual '{value}'.");
            }
        }
    }

    private static DrawingPlan ReadPlan(string planJson)
    {
        return JsonSerializer.Deserialize<DrawingPlan>(planJson, PlanJson.Options)
            ?? throw new CadOperationException("INVALID_PLAN", "The stored drawing plan could not be read.");
    }

    private static DrawingPlan ReplaceHoleDiameter(
        DrawingPlan plan,
        double newHoleDiameter,
        Guid requestId,
        string documentFingerprint)
    {
        return plan with
        {
            RequestId = requestId,
            DocumentFingerprint = documentFingerprint,
            Steps = plan.Steps.Select(step => step is FourHolePatternStep holes
                    ? holes with { Diameter = newHoleDiameter }
                    : step)
                .ToArray()
        };
    }

    private static void RequireValid(DrawingPlan plan)
    {
        var validation = PlanValidator.Validate(plan);
        if (!validation.IsValid)
        {
            throw new CadOperationException(
                "INVALID_PLAN",
                string.Join("; ", validation.Errors.Select(error => $"{error.Path}: {error.Message}")));
        }
    }

    private static CreateResult ToCreateResult(BatchMetadata metadata, long elapsedMilliseconds)
    {
        return new CreateResult(
            true,
            metadata.BatchId,
            metadata.Handles,
            metadata.Warnings,
            elapsedMilliseconds);
    }

    internal sealed class UndoScope : IDisposable
    {
        private readonly System.Reflection.MethodInfo? _setUndoMark;
        private readonly bool _started;

        public UndoScope()
        {
            if (string.Equals(
                    Process.GetCurrentProcess().ProcessName,
                    "accoreconsole",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var utilsType = Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcMgd", throwOnError: true)
                ?? throw new InvalidOperationException("AutoCAD undo services are unavailable.");
            _setUndoMark = utilsType.GetMethod("SetUndoMark", new[] { typeof(bool) })
                ?? throw new InvalidOperationException("AutoCAD undo services are unavailable.");
            _started = (bool)(_setUndoMark.Invoke(null, new object[] { true }) ?? false);
        }

        public void Dispose()
        {
            if (_started)
            {
                _setUndoMark!.Invoke(null, new object[] { false });
            }
        }
    }
}
