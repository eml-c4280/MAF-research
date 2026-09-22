using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IAgentRunLogRepository
{
    Task<AgentRunLog?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AgentRunLog>> GetAllAsync(CancellationToken ct = default);

    /// <summary>All turns of one conversation session, ordered oldest-first (chat display order,
    /// unlike GetAllAsync's most-recent-first audit ordering). docs/plan.md section 14.</summary>
    Task<IReadOnlyList<AgentRunLog>> GetByConversationSessionIdAsync(int conversationSessionId, CancellationToken ct = default);

    Task AddAsync(AgentRunLog log, CancellationToken ct = default);
    Task UpdateAsync(AgentRunLog log, CancellationToken ct = default);
}
