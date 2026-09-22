using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class InsurancePolicyRepository : IInsurancePolicyRepository
{
    private readonly AgentCoreDbContext _db;

    public InsurancePolicyRepository(AgentCoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default) =>
        await _db.InsurancePolicies.AsNoTracking()
            .Where(p => scopedToCaseManagerUserId == null || p.Worker!.AssignedCaseManagerUserId == scopedToCaseManagerUserId)
            .OrderBy(p => p.PolicyNumber)
            .ToListAsync(ct);

    public Task<InsurancePolicy?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.InsurancePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<InsurancePolicy?> GetByWorkerIdAsync(int workerId, CancellationToken ct = default) =>
        _db.InsurancePolicies.AsNoTracking()
            .Where(p => p.WorkerId == workerId)
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.EndDate)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        _db.InsurancePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        _db.InsurancePolicies.Update(policy);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var policy = await _db.InsurancePolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return false;
        }

        _db.InsurancePolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
