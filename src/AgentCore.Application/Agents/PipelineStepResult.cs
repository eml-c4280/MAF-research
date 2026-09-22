using AgentCore.Domain.Entities;

namespace AgentCore.Application.Agents;

/// <summary>One executed step of ClaimAgentService.RunAgentPipelineAsync (Phase 13,
/// docs/plan-agents.md §7) - the WorkflowStepDefinition it came from, the AgentRunLog it
/// produced, and the actual prompt sent (after {input}/{steps.OutputKey} placeholder
/// substitution) for audit.</summary>
public record PipelineStepResult(WorkflowStepDefinition Step, AgentRunLog Run, string ResolvedPrompt);
