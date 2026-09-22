using AgentCore.Domain.Entities;

namespace AgentCore.Application.Workflows;

public enum WorkflowRunStatus
{
    NotFound,
    ValidationError,
    /// <summary>The underlying claim is Disputed (only reachable via the "Process Claim" workflow,
    /// which delegates to ClaimAgentService.ProcessClaimAsync's own hard block).</summary>
    Blocked,
    /// <summary>The caller lacks permission for the underlying worker (docs/plan.md section 5,
    /// WorkerAccessPolicy) - distinct from Blocked, which is a business rule outcome.</summary>
    Forbidden,
    Completed
}

public record WorkflowRunOutcome(
    WorkflowRunStatus Status, string? Error, WorkflowDefinition? Definition, WorkflowRun? WorkflowRun, AgentRunLog? Run)
{
    public static WorkflowRunOutcome NotFoundResult(string? error = null) => new(WorkflowRunStatus.NotFound, error, null, null, null);
    public static WorkflowRunOutcome ValidationErrorResult(string error) => new(WorkflowRunStatus.ValidationError, error, null, null, null);
    public static WorkflowRunOutcome BlockedResult(string reason) => new(WorkflowRunStatus.Blocked, reason, null, null, null);
    public static WorkflowRunOutcome ForbiddenResult(string reason) => new(WorkflowRunStatus.Forbidden, reason, null, null, null);
    public static WorkflowRunOutcome CompletedResult(WorkflowDefinition definition, WorkflowRun workflowRun, AgentRunLog run) =>
        new(WorkflowRunStatus.Completed, null, definition, workflowRun, run);
}

/// <summary>docs/plan.md §11: chat is a routing convenience over the structured path, never a
/// second execution engine - below a confidence threshold, or if a required input can't be
/// resolved, it falls back to ClaimAgentService.QueryAsync rather than guessing.</summary>
public record WorkflowChatOutcome(bool FellBackToQuery, WorkflowRunOutcome? WorkflowResult, AgentRunLog? FallbackRun);
