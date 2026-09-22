using System.ComponentModel;
using AgentCore.Domain.Repositories;
using AgentCore.Domain.Rules;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>
/// Rule tool (docs/business-logic.md §4, CV family): deterministic, not a suggestion. The agent
/// may call this and must quote the result exactly - it cannot override or recompute a coverage
/// verdict. Read-only (no PendingAction, no side effect), so it's available to both the read-only
/// and claim-processing agents, same as the existing auto tools. Row-level scoped (docs/plan.md
/// section 5): denies a CaseManager caller a claim whose worker is not assigned to them.
/// </summary>
[McpServerToolType]
public class CheckCoverageTool
{
    private readonly IClaimRepository _claims;
    private readonly IInsurancePolicyRepository _policies;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public CheckCoverageTool(IClaimRepository claims, IInsurancePolicyRepository policies, IWorkerRepository workers, CallerContext caller)
    {
        _claims = claims;
        _policies = policies;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "CoverageChecker", ReadOnly = true)]
    [Description("Deterministically checks whether a claim is covered by the worker's insurance policy - policy period (CV-1), claim type match (CV-2), cumulative cover limit (CV-3), and a policy data-integrity flag (CV-4). This is a rule tool: quote its result exactly, never override or recompute it. Use before recommending a payout or before deciding a claim should be declined for coverage reasons.")]
    public async Task<string> CheckCoverageAsync(
        [Description("The Id of the claim to check coverage for.")] int claimId)
    {
        var (claim, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return denial;
        }

        var policy = await _policies.GetByWorkerIdAsync(claim!.WorkerId);
        var history = await _claims.GetByWorkerIdAsync(claim.WorkerId, DateOnly.MinValue);
        var result = CoverageValidator.Validate(claim, policy, history);

        var verdict = result.Passed ? "COVERED" : $"NOT COVERED ({result.RejectionReason})";
        return $"Coverage check for claim #{claimId}: {verdict}\n" + string.Join("\n", result.Details);
    }
}
