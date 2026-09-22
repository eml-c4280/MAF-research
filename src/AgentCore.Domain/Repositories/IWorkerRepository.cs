using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IWorkerRepository
{
    /// <summary>docs/plan.md section 5: when <paramref name="scopedToCaseManagerUserId"/> is set,
    /// only workers assigned to that user are returned (a SQL filter, not an in-memory one) - null
    /// means unrestricted (Admin/SuperAdmin).</summary>
    Task<IReadOnlyList<Worker>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default);

    Task<Worker?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Finds a worker by Code, numeric Id, or a partial/full name match, in that order.</summary>
    Task<Worker?> FindByCodeOrIdOrNameAsync(string identifier, CancellationToken ct = default);

    Task AddAsync(Worker worker, CancellationToken ct = default);
    Task UpdateAsync(Worker worker, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
