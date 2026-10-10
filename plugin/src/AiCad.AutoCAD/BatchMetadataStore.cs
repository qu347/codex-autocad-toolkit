using System.Text.Json;
using AiCad.Contracts;
using Autodesk.AutoCAD.DatabaseServices;

namespace AiCad.AutoCAD;

internal sealed record BatchMetadata(
    string SchemaVersion,
    Guid RequestId,
    string BatchId,
    OperationKind Operation,
    DateTimeOffset CompletedAt,
    string PlanJson,
    IReadOnlyDictionary<string, string> Handles,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string>? EntitySnapshots = null,
    string? PreviousBatchId = null,
    string? PreviousPlanJson = null,
    bool IsUndone = false);

internal sealed class BatchMetadataStore
{
    private const string RootKey = "AICAD";
    private const string EntityMetadataKey = "AICAD_META";
    private const int ChunkLength = 2000;

    public BatchMetadata? FindByRequest(Database database, Transaction transaction, Guid requestId)
    {
        var root = OpenRoot(database, transaction, create: false);
        if (root is null)
        {
            return null;
        }

        var requestKey = RequestKey(requestId);
        if (!root.Contains(requestKey))
        {
            return null;
        }

        var batchId = ReadText((Xrecord)transaction.GetObject(root.GetAt(requestKey), OpenMode.ForRead));
        return ReadBatch(root, transaction, batchId);
    }

    public BatchMetadata? FindByBatch(Database database, Transaction transaction, string batchId)
    {
        var root = OpenRoot(database, transaction, create: false);
        return root is null ? null : ReadBatch(root, transaction, batchId);
    }

    public string? FindLatestBatchId(Database database, Transaction transaction)
    {
        var root = OpenRoot(database, transaction, create: false);
        return root is null || !root.Contains("LATEST_BATCH")
            ? null
            : ReadText((Xrecord)transaction.GetObject(root.GetAt("LATEST_BATCH"), OpenMode.ForRead));
    }

    public void Save(
        Database database,
        Transaction transaction,
        BatchMetadata metadata,
        IReadOnlyDictionary<string, ObjectId> entities)
    {
        var root = OpenRoot(database, transaction, create: true)
            ?? throw new InvalidOperationException("AI-CAD metadata dictionary could not be created.");
        WriteText(root, transaction, BatchKey(metadata.BatchId), JsonSerializer.Serialize(metadata, PlanJson.Options));
        WriteText(root, transaction, RequestKey(metadata.RequestId), metadata.BatchId);
        WriteText(root, transaction, "LATEST_BATCH", metadata.BatchId);

        foreach (var pair in entities)
        {
            var entity = (Entity)transaction.GetObject(pair.Value, OpenMode.ForWrite);
            entity.CreateExtensionDictionary();
            var extension = (DBDictionary)transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
            WriteText(
                extension,
                transaction,
                EntityMetadataKey,
                JsonSerializer.Serialize(
                    new EntityMetadata(metadata.BatchId, pair.Key),
                    PlanJson.Options));
        }
    }

    public void MarkUndone(
        Database database,
        Transaction transaction,
        BatchMetadata metadata)
    {
        var root = OpenRoot(database, transaction, create: true)
            ?? throw new InvalidOperationException("AI-CAD metadata dictionary could not be opened.");
        WriteText(
            root,
            transaction,
            BatchKey(metadata.BatchId),
            JsonSerializer.Serialize(metadata with { IsUndone = true }, PlanJson.Options));
    }

    private static BatchMetadata? ReadBatch(
        DBDictionary root,
        Transaction transaction,
        string batchId)
    {
        var batchKey = BatchKey(batchId);
        if (!root.Contains(batchKey))
        {
            return null;
        }

        var json = ReadText((Xrecord)transaction.GetObject(root.GetAt(batchKey), OpenMode.ForRead));
        return JsonSerializer.Deserialize<BatchMetadata>(json, PlanJson.Options);
    }

    private static DBDictionary? OpenRoot(Database database, Transaction transaction, bool create)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (namedObjects.Contains(RootKey))
        {
            return (DBDictionary)transaction.GetObject(namedObjects.GetAt(RootKey), OpenMode.ForRead);
        }

        if (!create)
        {
            return null;
        }

        namedObjects.UpgradeOpen();
        var root = new DBDictionary();
        namedObjects.SetAt(RootKey, root);
        transaction.AddNewlyCreatedDBObject(root, true);
        return root;
    }

    private static void WriteText(
        DBDictionary dictionary,
        Transaction transaction,
        string key,
        string value)
    {
        Xrecord record;
        if (dictionary.Contains(key))
        {
            record = (Xrecord)transaction.GetObject(dictionary.GetAt(key), OpenMode.ForWrite);
        }
        else
        {
            if (!dictionary.IsWriteEnabled)
            {
                dictionary.UpgradeOpen();
            }

            record = new Xrecord();
            dictionary.SetAt(key, record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }

        var chunks = Enumerable.Range(0, (value.Length + ChunkLength - 1) / ChunkLength)
            .Select(index => new TypedValue(
                (int)DxfCode.Text,
                value.Substring(index * ChunkLength, Math.Min(ChunkLength, value.Length - (index * ChunkLength)))))
            .ToArray();
        record.Data = new ResultBuffer(chunks);
    }

    private static string ReadText(Xrecord record)
    {
        return string.Concat(record.Data
            .AsArray()
            .Where(value => value.TypeCode == (int)DxfCode.Text)
            .Select(value => value.Value as string));
    }

    private static string BatchKey(string batchId) => $"BATCH_{batchId}";

    private static string RequestKey(Guid requestId) => $"REQUEST_{requestId:N}";

    private sealed record EntityMetadata(string BatchId, string SemanticId);
}
