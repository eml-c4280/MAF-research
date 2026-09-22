namespace AgentCore.Domain.Entities;

/// <summary>
/// Audit row for one executed step of a multi-agent WorkflowRun (Phase 13, docs/plan-agents.md) -
/// every specialist's invocation gets its own AgentRunLog, exactly as fully audited as a
/// standalone agent call is anywhere else in this app; this is just the join between "which step,
/// which agent" and "which AgentRunLog it produced".
/// </summary>
public class WorkflowRunStep
{
    public int Id { get; set; }
    public int WorkflowRunId { get; set; }
    public WorkflowRun? WorkflowRun { get; set; }

    public int StepIndex { get; set; }
    public string AgentName { get; set; } = string.Empty;

    public int AgentRunLogId { get; set; }
    public AgentRunLog? AgentRunLog { get; set; }

    public string OutputKey { get; set; } = string.Empty;

    /// <summary>The actual prompt sent, after {input}/{steps.OutputKey} placeholder substitution -
    /// audit trail for what the step was actually asked, not just what template it came from.</summary>
    public string ResolvedPromptSnapshot { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
