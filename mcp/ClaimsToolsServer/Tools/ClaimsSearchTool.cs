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
    [Description("Search insurance claims across ALL workers, optionally filtered by claim type and/or status, within a recent number of years. Use this when the question is about claims in general (e.g. 'all Medical claims') rather than tied to one specific worker.")]
    public async Task<string> SearchClaimsAsync(
        [Description("Filter by claim type (e.g. 'Injury', 'Property Damage', 'Medical', 'Equipment Damage', 'Liability'). Omit to include all types.")] string? claimType = null,
        [Description("Filter by claim status (e.g. 'Approved', 'Rejected', 'Pending', 'UnderReview'). Omit to include all statuses.")] string? status = null,
        [Description("How many years back from today to include. Defaults to 3.")] int years = 3)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);
        var scope = _caller.IsAdminOrAbove ? null : _caller.UserId;
        var results = await _claims.SearchAsync(claimType, status, cutoff, scope);

        if (results.Count == 0)
        {
            return $"No claims found matching the given filters in the last {years} years" +
                   (scope is not null ? " among workers assigned to you." : ".");
        }

        var lines = new List<string>(results.Count);
        foreach (var c in results.OrderByDescending(c => c.ClaimDate))
        {
            var worker = await _workers.GetByIdAsync(c.WorkerId);
            var workerLabel = worker is null ? $"#{c.WorkerId}" : $"{worker.Code} {worker.Name}";
            lines.Add($"{c.ClaimNumber} | {workerLabel} | {c.ClaimType} | {c.Status} | {c.Amount:C} | {c.ClaimDate:yyyy-MM-dd}");
        }

        var totalAmount = results.Sum(c => c.Amount);

        return $"Found {results.Count} claim(s) in the last {years} years, total {totalAmount:C}:\n" +
               string.Join("\n", lines);
    }
}
