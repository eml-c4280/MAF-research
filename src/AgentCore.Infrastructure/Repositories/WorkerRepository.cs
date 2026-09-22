using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Repositories;

public class WorkerRepository : IWorkerRepository
{
    private readonly AgentCoreDbContext _db;

    public WorkerRepository(AgentCoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<Worker>> GetAllAsync(int? scopedToCaseManagerUserId = null, CancellationToken ct = default) =>
        await _db.Workers.AsNoTracking()
            .Where(w => scopedToCaseManagerUserId == null || w.AssignedCaseManagerUserId == scopedToCaseManagerUserId)
            .OrderBy(w => w.Code)
            .ToListAsync(ct);

    public Task<Worker?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Workers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<Worker?> FindByCodeOrIdOrNameAsync(string identifier, CancellationToken ct = default)
    {
        if (int.TryParse(identifier, out var id))
        {
            var byId = await _db.Workers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
            if (byId is not null)
            {
                return byId;
            }
        }

        var lowered = identifier.ToLower();

        var byCode = await _db.Workers.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Code.ToLower() == lowered, ct);
        if (byCode is not null)
        {
            return byCode;
        }

        return await _db.Workers.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Name.ToLower().Contains(lowered), ct);
    }

    public async Task AddAsync(Worker worker, CancellationToken ct = default)
    {
        _db.Workers.Add(worker);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Worker worker, CancellationToken ct = default)
    {
        _db.Workers.Update(worker);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (worker is null)
        {
            return false;
        }

        _db.Workers.Remove(worker);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
