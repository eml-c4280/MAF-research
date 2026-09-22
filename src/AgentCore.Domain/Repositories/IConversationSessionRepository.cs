using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IConversationSessionRepository
{
    Task<ConversationSession?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSession>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ConversationSession session, CancellationToken ct = default);
    Task UpdateAsync(ConversationSession session, CancellationToken ct = default);
}
