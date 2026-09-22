using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Entities;

/// <summary>
/// Thin metadata wrapper recording how/why one workflow execution was launched (docs/plan.md
/// §11) - the actual execution is still a single <see cref="AgentRunLog"/>, unchanged.
/// </summary>
public class WorkflowRun
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    public int AgentRunLogId { get; set; }
    public AgentRunLog? AgentRunLog { get; set; }

    /// <summary>The resolved inputs actually used, as JSON.</summary>
    public string InputValuesJson { get; set; } = string.Empty;

    public WorkflowTriggerSource TriggerSource { get; set; }

    /// <summary>The original free text, kept for audit when triggered via chat. Null for a structured trigger.</summary>
    public string? RawChatInput { get; set; }

    /// <summary>The intent-matcher's confidence when triggered via chat. Null for a structured trigger.</summary>
    public double? MatchConfidence { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
