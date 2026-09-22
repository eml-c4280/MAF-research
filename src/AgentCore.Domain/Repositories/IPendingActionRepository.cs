using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Repositories;

public interface IPendingActionRepository
{
    Task<PendingAction?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PendingAction>> GetByStatusAsync(PendingActionStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<PendingAction>> GetByAgentRunIdAsync(int agentRunLogId, CancellationToken ct = default);

    /// <summary>Idempotency check (docs/plan.md section 13): looks up an existing action by the
    /// key a sensitive tool computes from (ClaimId, ActionType, a short time bucket), so a
    /// retried tool call can return the existing PendingActionRef instead of queuing a duplicate.</summary>
    Task<PendingAction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    Task AddAsync(PendingAction action, CancellationToken ct = default);
    Task UpdateAsync(PendingAction action, CancellationToken ct = default);
}
