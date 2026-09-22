namespace ClaimsToolsServer.Tools;

/// <summary>
/// Computes a dedupe key for a sensitive tool call from (claim, action type, a short time
/// bucket) - see docs/plan.md section 13 ("Idempotency"). Deliberately not model-supplied: a
/// key the model itself could vary would defeat the point of catching a retried call.
/// </summary>
public static class PendingActionIdempotency
{
    private static readonly long BucketTicks = TimeSpan.FromMinutes(5).Ticks;

    public static string ComputeKey(int claimId, string actionType)
    {
        var bucket = DateTime.UtcNow.Ticks / BucketTicks;
        return $"{claimId}:{actionType}:{bucket}";
    }
}
