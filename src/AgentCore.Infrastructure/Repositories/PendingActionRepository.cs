using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class PendingActionRepository : IPendingActionRepository
{
    private readonly AgentCoreDbContext _db;

    public PendingActionRepository(AgentCoreDbContext db) => _db = db;

    public Task<PendingAction?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.PendingActions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<PendingAction>> GetByStatusAsync(PendingActionStatus status, CancellationToken ct = default) =>
        await _db.PendingActions.AsNoTracking()
            .Where(a => a.Status == status)
            .OrderBy(a => a.RequestedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PendingAction>> GetByAgentRunIdAsync(int agentRunLogId, CancellationToken ct = default) =>
        await _db.PendingActions.AsNoTracking()
            .Where(a => a.ProposedByAgentRunId == agentRunLogId)
            .ToListAsync(ct);

    public Task<PendingAction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.PendingActions.FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(PendingAction action, CancellationToken ct = default)
    {
        _db.PendingActions.Add(action);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PendingAction action, CancellationToken ct = default)
    {
        _db.PendingActions.Update(action);
        await _db.SaveChangesAsync(ct);
    }
}
