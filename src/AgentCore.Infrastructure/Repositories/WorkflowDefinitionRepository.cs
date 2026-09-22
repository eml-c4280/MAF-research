using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly AgentCoreDbContext _db;

    public WorkflowDefinitionRepository(AgentCoreDbContext db) => _db = db;

    public Task<WorkflowDefinition?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.WorkflowDefinitions.AsNoTracking().Include(w => w.Steps).FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<WorkflowDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
        _db.WorkflowDefinitions.AsNoTracking().Include(w => w.Steps).FirstOrDefaultAsync(w => w.Name == name, ct);

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default) =>
        await _db.WorkflowDefinitions.AsNoTracking().Include(w => w.Steps).OrderBy(w => w.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowDefinition>> GetChatTriggerableAsync(CancellationToken ct = default) =>
        await _db.WorkflowDefinitions.AsNoTracking().Include(w => w.Steps)
            .Where(w => w.IsActive && w.IsChatTriggerable)
            .ToListAsync(ct);

    public async Task AddAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        _db.WorkflowDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);
    }
}
