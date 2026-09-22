namespace AgentCore.Api.Contracts;

public record ApprovalBatchDecision(int Id, bool Approve);

public record ApprovalBatchRequest(IReadOnlyList<ApprovalBatchDecision> Decisions);

/// <summary>Full per-decision detail, never a single collapsed confirmation - a batch approval
/// pitfall called out in docs/knowledge-base.md (Human-in-the-Loop, section 8) and captured as a
/// decision in docs/plan.md section 13.</summary>
public record ApprovalBatchItemResult(int Id, string Outcome, PendingActionDto? Action, string? Error);
