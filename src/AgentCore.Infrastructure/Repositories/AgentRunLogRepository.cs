using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class AgentRunLogRepository : IAgentRunLogRepository
{
    private readonly AgentCoreDbContext _db;

    public AgentRunLogRepository(AgentCoreDbContext db) => _db = db;

    public Task<AgentRunLog?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.AgentRunLogs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<AgentRunLog>> GetAllAsync(CancellationToken ct = default) =>
        await _db.AgentRunLogs.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<AgentRunLog>> GetByConversationSessionIdAsync(int conversationSessionId, CancellationToken ct = default) =>
        await _db.AgentRunLogs.AsNoTracking()
            .Where(r => r.ConversationSessionId == conversationSessionId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(AgentRunLog log, CancellationToken ct = default)
    {
        _db.AgentRunLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AgentRunLog log, CancellationToken ct = default)
    {
        _db.AgentRunLogs.Update(log);
        await _db.SaveChangesAsync(ct);
    }
}
