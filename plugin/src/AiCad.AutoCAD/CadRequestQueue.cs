using System.Collections.Concurrent;
using AiCad.Contracts;

namespace AiCad.AutoCAD;

internal sealed record QueuedCadRequest(
    CadRpcRequest Request,
    TaskCompletionSource<CadRpcResponse> Completion);

internal sealed class CadRequestQueue
{
    private readonly ConcurrentQueue<QueuedCadRequest> _items = new();

    public void Enqueue(QueuedCadRequest item)
    {
        _items.Enqueue(item);
    }

    public bool TryDequeue(out QueuedCadRequest? item)
    {
        return _items.TryDequeue(out item);
    }
}
