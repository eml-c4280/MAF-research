using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IInsurancePolicyRepository
{
    /// <summary>docs/plan.md section 5: when <paramref name="scopedToCaseManagerUserId"/> is set,
    /// only policies whose worker is assigned to that user are returned.</summary>
    Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default);
    Task<InsurancePolicy?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<InsurancePolicy?> GetByWorkerIdAsync(int workerId, CancellationToken ct = default);
    Task AddAsync(InsurancePolicy policy, CancellationToken ct = default);
    Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
