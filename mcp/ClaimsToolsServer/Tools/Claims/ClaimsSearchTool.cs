using System.ComponentModel;
using AgentCore.Domain.Repositories;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>Auto tool: read-only, safe for the agent to call without approval. Row-level scoped
/// (docs/plan.md section 5): a CaseManager caller only ever sees claims for workers assigned to
/// them - filtered at the repository/SQL level, not fetched-then-filtered.</summary>
[McpServerToolType]
public class ClaimsSearchTool
{
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public ClaimsSearchTool(IClaimRepository claims, IWorkerRepository workers, CallerContext caller)
    {
        _claims = claims;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "ClaimsSearcher", ReadOnly = true)]
    [Description("Search insurance claims, optionally filtered by claim type, status, and/or one specific worker, within a recent number of years. Pass 'worker' to scope results to a single worker's claims (matched server-side, not by reading worker names/codes out of the results yourself) - use this whenever you already know which worker you care about, rather than searching all workers and trying to pick out their row.")]
    public async Task<string> SearchClaimsAsync(
        [Description("Filter by claim type (e.g. 'Injury', 'Property Damage', 'Medical', 'Equipment Damage', 'Liability'). Omit to include all types.")] string? claimType = null,
        [Description("Filter by claim status (e.g. 'Approved', 'Rejected', 'Pending', 'UnderReview'). Omit to include all statuses.")] string? status = null,
        [Description("How many years back from today to include. Defaults to 3.")] int years = 3,
        [Description("Scope results to ONE worker's claims only - their Code (e.g. 'WRK-1004'), numeric ID, or full/partial name. Omit to search across all workers.")] string? worker = null)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);
        var scope = _caller.IsAdminOrAbove ? null : _caller.UserId;
        var results = await _claims.SearchAsync(claimType, status, cutoff, scope);

        if (!string.IsNullOrWhiteSpace(worker))
        {
            var matchedWorker = await _workers.FindByCodeOrIdOrNameAsync(worker);
            if (matchedWorker is null)
            {
                return $"No worker found matching '{worker}'.";
            }

            // Scoped here in C#, deterministically - never leave the small model to eyeball an
            // industry-wide result list and self-match the right worker's row (it has gotten
            // this wrong in practice, queuing an action against a different worker's claim).
            results = results.Where(c => c.WorkerId == matchedWorker.Id).ToList();
        }

        if (results.Count == 0)
        {
            return $"No claims found matching the given filters in the last {years} years" +
                   (worker is not null ? $" for worker '{worker}'." : scope is not null ? " among workers assigned to you." : ".");
        }

        var lines = new List<string>(results.Count);
        foreach (var c in results.OrderByDescending(c => c.ClaimDate))
        {
            var claimWorker = await _workers.GetByIdAsync(c.WorkerId);
            var workerLabel = claimWorker is null ? $"#{c.WorkerId}" : $"{claimWorker.Code} {claimWorker.Name}";
            // Id (not just ClaimNumber) is required here - CoverageChecker/PayoutCalculator/etc.
            // all take the numeric Id, and this was previously the only tool that lists individual
            // claims at all, with no way to get from a search result to a callable claim Id.
            lines.Add($"Id {c.Id} | {c.ClaimNumber} | {workerLabel} | {c.ClaimType} | {c.Status} | {c.Amount:C} | {c.ClaimDate:yyyy-MM-dd}");
        }

        var totalAmount = results.Sum(c => c.Amount);

        return $"Found {results.Count} claim(s) in the last {years} years, total {totalAmount:C}:\n" +
               string.Join("\n", lines);
    }
}
