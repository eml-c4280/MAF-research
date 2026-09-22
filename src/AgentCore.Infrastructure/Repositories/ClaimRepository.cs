using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class ClaimRepository : IClaimRepository
{
    private readonly AgentCoreDbContext _db;

    public ClaimRepository(AgentCoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<Claim>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default) =>
        await _db.Claims.AsNoTracking()
            .Where(c => scopedToCaseManagerUserId == null || c.Worker!.AssignedCaseManagerUserId == scopedToCaseManagerUserId)
            .OrderByDescending(c => c.ClaimDate)
            .ToListAsync(ct);

    public Task<Claim?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Claim>> SearchAsync(
        string? claimType, string? status, DateOnly since, int? scopedToCaseManagerUserId = null, CancellationToken ct = default)
    {
        var query = _db.Claims.AsNoTracking().Where(c => c.ClaimDate >= since);

        if (!string.IsNullOrWhiteSpace(claimType))
        {
            query = query.Where(c => c.ClaimType.ToLower() == claimType.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ClaimStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (scopedToCaseManagerUserId is not null)
        {
            query = query.Where(c => c.Worker!.AssignedCaseManagerUserId == scopedToCaseManagerUserId);
        }

        return await query.OrderByDescending(c => c.ClaimDate).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Claim>> GetByWorkerIdAsync(int workerId, DateOnly since, CancellationToken ct = default) =>
        await _db.Claims.AsNoTracking()
            .Where(c => c.WorkerId == workerId && c.ClaimDate >= since)
            .OrderByDescending(c => c.ClaimDate)
            .ToListAsync(ct);

    public async Task AddAsync(Claim claim, CancellationToken ct = default)
    {
        _db.Claims.Add(claim);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Claim claim, CancellationToken ct = default)
    {
        _db.Claims.Update(claim);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var claim = await _db.Claims.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null)
        {
            return false;
        }

        _db.Claims.Remove(claim);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
