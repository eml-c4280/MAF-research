using AgentCore.Domain.Entities;

namespace AgentCore.Application.Approvals;

public enum ApprovalOutcome
{
    NotFound,
    AlreadyDecided,
    Success,

    /// <summary>The action's ExpiresAt has passed - ApproveAsync refuses rather than blindly
    /// executing a stale action (see docs/plan.md section 13, "HITL gaps"). RejectAsync is never
    /// blocked by expiry, since rejecting has no side effect.</summary>
    Expired
}

public record ApprovalResult(ApprovalOutcome Outcome, PendingAction? Action);
