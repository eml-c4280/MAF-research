using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Repositories;

public interface IClaimRepository
{
    /// <summary>docs/plan.md section 5: when <paramref name="scopedToCaseManagerUserId"/> is set,
    /// only claims whose worker is assigned to that user are returned - null means unrestricted.</summary>
    Task<IReadOnlyList<Claim>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default);

    Task<Claim?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Claims across all workers, optionally filtered by type/status, on or after
    /// <paramref name="since"/>; scoped the same way as GetAllAsync.</summary>
    Task<IReadOnlyList<Claim>> SearchAsync(string? claimType, string? status, DateOnly since, int? scopedToCaseManagerUserId = null, CancellationToken ct = default);

    /// <summary>Claims for one worker, on or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<Claim>> GetByWorkerIdAsync(int workerId, DateOnly since, CancellationToken ct = default);

    Task AddAsync(Claim claim, CancellationToken ct = default);
    Task UpdateAsync(Claim claim, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
