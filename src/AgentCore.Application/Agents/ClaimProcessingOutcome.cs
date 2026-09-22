namespace AgentCore.Application.Agents;

public enum ClaimProcessingStatus
{
    NotFound,
    /// <summary>Hard-blocked, e.g. the claim is Disputed (docs/business-logic.md §3) - no agent
    /// run was attempted at all.</summary>
    Blocked,
    /// <summary>The caller (a CaseManager) is not permitted to access this claim's worker
    /// (docs/plan.md section 5, WorkerAccessPolicy) - distinct from Blocked, which is a business
    /// rule outcome, not a permission denial.</summary>
    Forbidden,
    Completed
}

public record ClaimProcessingOutcome(ClaimProcessingStatus Status, string? BlockReason, ClaimProcessingResult? Result);
