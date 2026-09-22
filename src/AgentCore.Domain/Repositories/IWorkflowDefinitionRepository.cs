using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Phase 13 (docs/plan-agents.md §7): looks up "Process Claim" (or any other
    /// well-known definition) by name, since ClaimAgentService/WorkflowExecutionService's
    /// dedicated ProcessClaimAsync entry point has no WorkflowDefinitionId to look up by.</summary>
    Task<WorkflowDefinition?> GetByNameAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Only definitions with IsActive and IsChatTriggerable set - the candidate set for
    /// the chat intent-matcher.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> GetChatTriggerableAsync(CancellationToken ct = default);

    Task AddAsync(WorkflowDefinition definition, CancellationToken ct = default);
}
