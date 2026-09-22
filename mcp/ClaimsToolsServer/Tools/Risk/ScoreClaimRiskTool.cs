using System.ComponentModel;
using AgentCore.Domain.Repositories;
using AgentCore.Domain.Rules;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>
/// Rule tool (docs/business-logic.md §4, FR family, code-computable subset): deterministic
/// fraud/anomaly signal flags. These never auto-decline anything - they're evidence for a human
/// to weigh, and must never be shown to the worker. Read-only (no PendingAction, no side effect).
/// Row-level scoped (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is
/// not assigned to them.
/// </summary>
[McpServerToolType]
public class ScoreClaimRiskTool
{
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public ScoreClaimRiskTool(IClaimRepository claims, IWorkerRepository workers, CallerContext caller)
    {
        _claims = claims;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "ClaimRiskScorer", ReadOnly = true)]
    [Description("Deterministically flags fraud/anomaly risk signals for a claim with evidence - long incident-to-report lag (FR-1), repeated claims by the same worker (FR-4), and a Monday-incident pattern (FR-5, weak alone). These are flags for a human to weigh, never grounds to auto-decline and never to be shown to the worker. Also separately read the claim description yourself for inconsistencies with the injury type/role/mechanism (FR-3) - that's your own judgement to make, not this tool's.")]
    public async Task<string> ScoreClaimRiskAsync(
        [Description("The Id of the claim to score.")] int claimId)
    {
        var (claim, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return denial;
        }

        var history = await _claims.GetByWorkerIdAsync(claim!.WorkerId, DateOnly.MinValue);
        var result = ClaimRiskScorer.Score(claim, history);

        return result.Flags.Count == 0
            ? $"No risk flags for claim #{claimId}."
            : $"Risk flags for claim #{claimId}:\n" + string.Join("\n", result.Flags.Select(f => $"- {f.RuleId}: {f.Evidence}"));
    }
}
