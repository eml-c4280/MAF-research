namespace ClaimsToolsServer.Tools;

/// <summary>
/// Returned by CalculatePayoutTool instead of PendingActionRef, since a payout call can also be
/// refused (coverage failed) rather than queued. When Blocked is false, PendingActionId/
/// ComputedAmount are set and this carries the same shape ClaimAgentService.
/// BackfillPendingActionRunIdAsync already looks for (a top-level "pendingActionId" property), so
/// no changes were needed there. When Blocked is true, PendingActionId is null - the backfill
/// code just logs a harmless warning and moves on, which is the correct behavior for a refusal.
/// </summary>
public record PayoutCalculationResult(int? PendingActionId, string ActionType, decimal? ComputedAmount, bool Blocked, string? Reason);
