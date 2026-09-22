using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class WorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AgentCoreDbContext _db;

    public WorkflowRunRepository(AgentCoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<WorkflowRun>> GetAllAsync(CancellationToken ct = default) =>
        await _db.WorkflowRuns.AsNoTracking()
            .Include(r => r.WorkflowDefinition)
            .Include(r => r.AgentRunLog)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowRun run, CancellationToken ct = default)
    {
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);
    }
}
