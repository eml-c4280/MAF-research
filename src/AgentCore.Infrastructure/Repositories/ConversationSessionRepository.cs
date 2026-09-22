using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class ConversationSessionRepository : IConversationSessionRepository
{
    private readonly AgentCoreDbContext _db;

    public ConversationSessionRepository(AgentCoreDbContext db) => _db = db;

    public Task<ConversationSession?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.ConversationSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<ConversationSession>> GetAllAsync(CancellationToken ct = default) =>
        await _db.ConversationSessions.AsNoTracking()
            .OrderByDescending(s => s.LastActivityAtUtc)
            .ToListAsync(ct);

    public async Task AddAsync(ConversationSession session, CancellationToken ct = default)
    {
        _db.ConversationSessions.Add(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ConversationSession session, CancellationToken ct = default)
    {
        _db.ConversationSessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }
}
