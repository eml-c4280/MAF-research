using System.ComponentModel;
using AgentCore.Domain.Repositories;
using AgentCore.Domain.Rules;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>
/// Rule tool (docs/business-logic.md §4, ES family): deterministic escalation-trigger check.
/// Read-only (no PendingAction, no side effect). If this reports a trigger, the claim-processing
/// agent's SendWorkerEmail/CalculatePayout tools are also removed from its toolset for that run by
/// ClaimAgentService - see docs/business-logic.md §5 ("if triggered, the agent's sensitive-tool
/// calls are rejected and the claim is routed to a human tier instead"). Row-level scoped
/// (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is not assigned to them.
/// </summary>
[McpServerToolType]
public class EvaluateEscalationTool
{
    private readonly IClaimRepository _claims;
    private readonly IInsurancePolicyRepository _policies;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public EvaluateEscalationTool(IClaimRepository claims, IInsurancePolicyRepository policies, IWorkerRepository workers, CallerContext caller)
    {
        _claims = claims;
        _policies = policies;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "EscalationEvaluator", ReadOnly = true)]
    [Description("Deterministically checks whether a claim meets any escalation trigger - amount threshold (ES-1), disputed status (ES-3), repeat claims within 12 months (ES-5), or a coverage failure on a claim that's already been paid (ES-6, a recovery case, not a decline). Quote its result exactly; never decide escalation yourself.")]
    public async Task<string> EvaluateEscalationAsync(
        [Description("The Id of the claim to evaluate for escalation.")] int claimId)
    {
        var (claim, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return denial;
        }

        var policy = await _policies.GetByWorkerIdAsync(claim!.WorkerId);
        var history = await _claims.GetByWorkerIdAsync(claim.WorkerId, DateOnly.MinValue);
        var coverage = CoverageValidator.Validate(claim, policy, history);
        var result = EscalationEvaluator.Evaluate(claim, history, coverage);

        return result.Triggered
            ? $"Escalation TRIGGERED for claim #{claimId}: {string.Join(", ", result.TriggeredRuleIds)} -> route to {result.Tier} tier."
            : $"No escalation triggers for claim #{claimId}.";
    }
}
