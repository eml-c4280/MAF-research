using System.ComponentModel;
using AgentCore.Domain.Authorization;
using AgentCore.Domain.Repositories;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>Auto tool: read-only, safe for the agent to call without approval. Row-level scoped
/// (docs/plan.md section 5): denies a CaseManager caller a worker not assigned to them.</summary>
[McpServerToolType]
public class WorkerClaimsHistoryTool
{
    private readonly IWorkerRepository _workers;
    private readonly IClaimRepository _claims;
    private readonly CallerContext _caller;

    public WorkerClaimsHistoryTool(IWorkerRepository workers, IClaimRepository claims, CallerContext caller)
    {
        _workers = workers;
        _claims = claims;
        _caller = caller;
    }

    [McpServerTool(Name = "WorkerClaimsHistoryFetcher", ReadOnly = true)]
    [Description("Get a statistical summary (count, total amount, average, breakdown by year/status/type) of ONE specific worker's insurance claims history over a recent number of years, looked up by worker Code, ID, or name. Use this instead of ClaimsSearcher when the question is about a single worker's history/pattern rather than claims across all workers.")]
    public async Task<string> GetWorkerClaimsHistoryAsync(
        [Description("The worker's Code (e.g. 'ABC-901'), numeric ID, or full/partial name if neither is known.")] string workerId,
        [Description("How many years back from today to include. Defaults to 3.")] int years = 3)
    {
        var worker = await _workers.FindByCodeOrIdOrNameAsync(workerId);
        if (worker is null)
        {
            return $"No worker found matching '{workerId}'.";
        }

        if (!WorkerAccessPolicy.CanAccessWorker(worker, _caller.CallerRoles, _caller.UserId))
        {
            return $"You do not have permission to access worker '{workerId}' - they are not assigned to you.";
        }

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);
        var claims = await _claims.GetByWorkerIdAsync(worker.Id, cutoff);

        if (claims.Count == 0)
        {
            return $"Worker {worker.Code} ({worker.Name}) has no claims in the last {years} years.";
        }

        var totalAmount = claims.Sum(c => c.Amount);
        var avgAmount = claims.Average(c => c.Amount);

        var byYear = claims
            .GroupBy(c => c.ClaimDate.Year)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Count()} claims, {g.Sum(c => c.Amount):C}");

        var byStatus = claims
            .GroupBy(c => c.Status)
            .Select(g => $"{g.Key}: {g.Count()}");

        var byType = claims
            .GroupBy(c => c.ClaimType)
            .Select(g => $"{g.Key}: {g.Count()}");

        return $"Worker {worker.Code} ({worker.Name}) claims history (last {years} years):\n" +
               $"Total claims: {claims.Count}, Total amount: {totalAmount:C}, Average per claim: {avgAmount:C}\n" +
               $"By year -> {string.Join("; ", byYear)}\n" +
               $"By status -> {string.Join("; ", byStatus)}\n" +
               $"By type -> {string.Join("; ", byType)}";
    }
}
