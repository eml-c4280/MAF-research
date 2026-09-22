namespace AgentCore.Domain.Entities;

/// <summary>
/// A named, reusable playbook admins configure ahead of time (docs/plan.md §11) - a fixed
/// template (prompt + input schema + allowed tool set), not a graph/DAG. No branching, looping,
/// or multi-step chains: a different tool set or prompt is a new row, not a new engine feature.
/// </summary>
public class WorkflowDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON array of {name, type, required, description} - e.g. [{"name":"claimId",
    /// "type":"int","required":true,"description":"..."}]. Types: "int", "string", "date".</summary>
    public string InputSchemaJson { get; set; } = string.Empty;

    /// <summary>{placeholder} slots filled from resolved inputs at run time.</summary>
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>JSON array of MCP tool names (e.g. ["WorkerInformationFetcher",
    /// "WorkerClaimsHistoryFetcher"]) - restricts the agent's tool set for this workflow to
    /// exactly this named subset, same enforcement mechanism as ClaimAgentService's escalation
    /// tool restriction (the model cannot call a tool it was never shown).</summary>
    public string AllowedToolNamesJson { get; set; } = string.Empty;

    public bool IsChatTriggerable { get; set; }

    /// <summary>JSON array of example phrases/keywords used by the chat intent-matcher.</summary>
    public string ChatTriggerHintsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
    public string CreatedByRole { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
