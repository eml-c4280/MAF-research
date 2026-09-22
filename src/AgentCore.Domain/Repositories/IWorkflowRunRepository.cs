using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IWorkflowRunRepository
{
    Task<IReadOnlyList<WorkflowRun>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(WorkflowRun run, CancellationToken ct = default);
}
