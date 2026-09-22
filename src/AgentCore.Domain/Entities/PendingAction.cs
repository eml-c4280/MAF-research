using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Entities;

public class PendingAction
{
    public int Id { get; set; }
    public int ClaimId { get; set; }
    public Claim? Claim { get; set; }
    public PendingActionType ActionType { get; set; }

    /// <summary>JSON payload describing the proposed action (e.g. email body, payout amount).</summary>
    public string Payload { get; set; } = string.Empty;

    public int? ProposedByAgentRunId { get; set; }
    public AgentRunLog? ProposedByAgentRun { get; set; }

    public PendingActionStatus Status { get; set; } = PendingActionStatus.AwaitingApproval;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? DecidedByRole { get; set; }
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>After this point, ApprovalService.ApproveAsync refuses to blindly approve the
    /// action (a human must notice it's stale) - docs/plan.md section 13 ("HITL gaps"). Rejecting
    /// a stale action is still allowed, since rejecting has no side effect to worry about.</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

    /// <summary>Computed server-side from (ClaimId, ActionType, a short time bucket) by the
    /// proposing tool, never model-supplied - deduplicates a retried sensitive-tool call so it
    /// doesn't queue a second PendingAction for the same logical action. Null for actions created
    /// before this field existed. See docs/plan.md section 13 ("Idempotency").</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Set alongside Status = ExecutionFailed when the approved side effect itself threw
    /// (e.g. the payout write-back failing) - captured so the failure is a visible, queryable
    /// state instead of an unhandled 500. Null otherwise.</summary>
    public string? ExecutionError { get; set; }

    /// <summary>JSON: the deterministic rule engine's basis for this action (e.g. EntitlementCalculator's
    /// RuleVersion + inputs for a CalculatePayout action) - so the approver sees "PY computed $X
    /// from these inputs", not just the model's prose. docs/business-logic.md §5, guardrail 2.
    /// Null for actions with no rule basis (e.g. a worker email).</summary>
    public string? RuleOutputsJson { get; set; }
}
