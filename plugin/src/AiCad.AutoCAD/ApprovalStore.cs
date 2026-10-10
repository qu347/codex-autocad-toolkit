using System.Security.Cryptography;
using System.Text;
using AiCad.Contracts;

namespace AiCad.AutoCAD;

internal sealed record PendingApproval(
    string TokenHash,
    Guid ChangeId,
    string DocumentFingerprint,
    string BatchId,
    double NewHoleDiameter,
    IReadOnlyDictionary<string, string> TargetHandleSnapshots,
    DateTimeOffset ExpiresAt);

internal sealed class CadOperationException : Exception
{
    public CadOperationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

internal sealed class ApprovalStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly Dictionary<Guid, PendingApproval> _pending = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    public ApprovalStore(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public ProposedChangeResult Create(
        string documentFingerprint,
        string batchId,
        double oldHoleDiameter,
        double newHoleDiameter,
        IReadOnlyDictionary<string, string> targetSnapshots)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var changeId = Guid.NewGuid();
        var expiresAt = _now().Add(Lifetime);
        var pending = new PendingApproval(
            Hash(token),
            changeId,
            documentFingerprint,
            batchId,
            newHoleDiameter,
            targetSnapshots,
            expiresAt);
        lock (_gate)
        {
            _pending[changeId] = pending;
        }

        return new ProposedChangeResult(
            changeId,
            token,
            $"Batch {batchId}: change four mounting holes from diameter {oldHoleDiameter:G} to diameter {newHoleDiameter:G}; rebuild the 3D solid, 2D views, and dimensions.",
            expiresAt);
    }

    public PendingApproval Consume(Guid changeId, string token)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(changeId, out var pending))
            {
                throw new CadOperationException("APPROVAL_REQUIRED", "No pending approval exists for this change.");
            }

            if (_now() > pending.ExpiresAt)
            {
                _pending.Remove(changeId);
                throw new CadOperationException("APPROVAL_EXPIRED", "The approval token has expired.");
            }

            if (!Matches(token, pending.TokenHash))
            {
                throw new CadOperationException("APPROVAL_REQUIRED", "The approval token is invalid.");
            }

            _pending.Remove(changeId);
            return pending;
        }
    }

    public CancelChangeResult Cancel(Guid changeId, string token)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(changeId, out var pending) || !Matches(token, pending.TokenHash))
            {
                throw new CadOperationException("APPROVAL_REQUIRED", "The pending approval was not found or the token is invalid.");
            }

            _pending.Remove(changeId);
            return new CancelChangeResult(true);
        }
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static bool Matches(string token, string expectedHash)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
