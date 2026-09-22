using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AgentCore.Domain.Diagnostics;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using AgentCore.Domain.Rules;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>
/// Sensitive tool: never applies a payout itself. The amount is computed here, deterministically,
/// by EntitlementCalculator - it is NOT a parameter the model supplies, which is the core fix
/// docs/business-logic.md argues for (§1: "the amount should come from a rule tool; the sensitive
/// tool should only queue applying the already-computed amount"). Refuses outright (no
/// PendingAction queued) if CoverageValidator fails, per docs/business-logic.md §7 ("downstream
/// rules don't run" once coverage fails). ProposedByAgentRunId is left unset here and backfilled
/// by the host after observing this tool's result - see docs/plan-mcp.md section 4. Row-level
/// scoped (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is not
/// assigned to them, using the same Blocked/Reason shape as a coverage refusal.
/// </summary>
[McpServerToolType]
public class CalculatePayoutTool
{
    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IInsurancePolicyRepository _policies;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public CalculatePayoutTool(
        IPendingActionRepository pendingActions, IClaimRepository claims, IInsurancePolicyRepository policies,
        IWorkerRepository workers, CallerContext caller)
    {
        _pendingActions = pendingActions;
        _claims = claims;
        _policies = policies;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "PayoutCalculator", Destructive = true)]
    [Description("Proposes applying a payout for this claim. The amount is computed deterministically by the rule engine - you do not supply it and cannot override it. This does NOT apply the payout — it queues it for Admin/CaseManager approval first. Refuses (no approval queued) if the claim fails coverage validation; call CoverageChecker first to see why.")]
    public async Task<PayoutCalculationResult> CalculatePayoutAsync(
        [Description("The Id of the claim this payout relates to.")] int claimId,
        [Description("Your justification for recommending a payout on this claim (not the amount - that's computed by the rule engine, not you).")] string justification)
    {
        var (claim, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return new PayoutCalculationResult(null, nameof(PendingActionType.CalculatePayout), null, true, denial);
        }

        var policy = await _policies.GetByWorkerIdAsync(claim!.WorkerId);
        var history = await _claims.GetByWorkerIdAsync(claim.WorkerId, DateOnly.MinValue);

        var coverage = CoverageValidator.Validate(claim, policy, history);
        if (!coverage.Passed)
        {
            return new PayoutCalculationResult(
                null, nameof(PendingActionType.CalculatePayout), null, true,
                $"Coverage check failed - no payout can be calculated: {coverage.RejectionReason}");
        }

        var idempotencyKey = PendingActionIdempotency.ComputeKey(claimId, nameof(PendingActionType.CalculatePayout));
        var existing = await _pendingActions.GetByIdempotencyKeyAsync(idempotencyKey);
        var entitlement = EntitlementCalculator.Calculate(claim, policy!, history);
        if (existing is not null)
        {
            return new PayoutCalculationResult(existing.Id, nameof(PendingActionType.CalculatePayout), entitlement.PayableAmount, false, null);
        }

        var ruleOutputsJson = JsonSerializer.Serialize(new
        {
            entitlement.RuleVersion,
            entitlement.Basis,
            coverageDetails = coverage.Details
        });

        var action = new PendingAction
        {
            ClaimId = claimId,
            ActionType = PendingActionType.CalculatePayout,
            Payload = JsonSerializer.Serialize(new { proposedAmount = entitlement.PayableAmount, justification }),
            Status = PendingActionStatus.AwaitingApproval,
            IdempotencyKey = idempotencyKey,
            RuleOutputsJson = ruleOutputsJson
        };

        await _pendingActions.AddAsync(action);
        AgentCoreDiagnostics.PendingActionsQueued.Add(1, new TagList { { "action_type", nameof(PendingActionType.CalculatePayout) } });
        return new PayoutCalculationResult(action.Id, nameof(PendingActionType.CalculatePayout), entitlement.PayableAmount, false, null);
    }
}
