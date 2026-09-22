namespace AgentCore.Domain.Entities;

/// <summary>
/// One ordered step of a multi-agent WorkflowDefinition (Phase 13, docs/plan-agents.md) - names
/// exactly one specialist agent (a key into AgentCore.Agents.AgentCatalog, not a DB-authored
/// value) and a prompt template for that step. A WorkflowDefinition with no steps runs on the
/// original, single-agent code path unchanged (docs/plan.md §11) - having any steps is what opts
/// a definition into the multi-agent path.
/// </summary>
public class WorkflowStepDefinition
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    /// <summary>Execution order, ascending.</summary>
    public int StepIndex { get; set; }

    /// <summary>A key into AgentCatalog (e.g. "ClaimsAgent") - which specialist runs this step.</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>{inputName} placeholders resolve from the workflow's top-level inputs; {steps.
    /// OutputKey} placeholders resolve from an earlier step's result - same simple string-replace
    /// FillTemplate already uses, just a second placeholder namespace.</summary>
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>The label later steps reference this step's result by, e.g. "ClaimDecision".</summary>
    public string OutputKey { get; set; } = string.Empty;
}
