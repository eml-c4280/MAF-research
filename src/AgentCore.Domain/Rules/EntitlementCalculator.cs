using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Rules;

/// <summary>
/// PY — Payout calculation (docs/business-logic.md §4, owner: Code — never the agent). This is
/// the architectural fix the whole business-logic proposal centers on: the amount comes from
/// here, not from whatever number the model proposes. <see cref="CalculatePayoutAmount"/> is
/// called directly by the sensitive CalculatePayout tool (mcp/ClaimsToolsServer) - never by the
/// agent, and never taking a model-supplied amount as input.
///
/// Deliberately simplified. This implements PY-1 (cap at available cover), PY-4 (employer
/// excess), and PY-6 (the result carries its rule version and inputs so it can be reproduced
/// exactly later) - it does NOT implement PY-2 (PIAWE-based weekly-benefit step-down) or PY-3
/// (offset for partial work capacity), because both genuinely require schema this pass doesn't
/// add (Worker.PIAWE, WorkCapacity, a tracked return-to-work date - see docs/business-logic.md §2
/// and §8, which itself says PY "needs PIAWE and the amount split first" and to do it last). PY-5
/// (third-party recovery flag) needs a ThirdPartyInvolved field, also not added. This calculator
/// answers "what's payable against this specific claim, capped by remaining policy cover, less
/// the standard excess" - not "what's this worker's weekly benefit entitled under the scheme."
/// </summary>
public static class EntitlementCalculator
{
    public const string RuleVersion = "PY-simplified-v1";

    // "Illustrative demo default" per docs/business-logic.md's own caution - a real scheme's
    // employer excess is set per-policy, not a single flat figure.
    public const decimal StandardExcess = 250m;

    public static EntitlementResult Calculate(Claim claim, InsurancePolicy policy, IReadOnlyList<Claim> priorClaimsUnderPolicy)
    {
        var priorTotal = priorClaimsUnderPolicy.Where(c => c.Id != claim.Id).Sum(c => c.Amount);
        var remainingCover = Math.Max(0, policy.CoverageAmount - priorTotal);
        var cappedAtCover = Math.Min(claim.Amount, remainingCover);
        var payable = Math.Max(0, cappedAtCover - StandardExcess);

        var basis =
            $"Claimed {claim.Amount:C}; prior claims against policy {policy.PolicyNumber} total {priorTotal:C} " +
            $"of {policy.CoverageAmount:C} cover, leaving {remainingCover:C} remaining; capped claimed amount at " +
            $"{cappedAtCover:C}; less standard excess {StandardExcess:C} = {payable:C} payable.";

        return new EntitlementResult(payable, RuleVersion, basis);
    }
}

/// <param name="PayableAmount">The deterministically computed amount - never model-supplied.</param>
/// <param name="RuleVersion">Persisted alongside the amount (PY-6) so it can be reproduced exactly later.</param>
/// <param name="Basis">Human-readable inputs/working, for the approver and for PY-6's audit requirement.</param>
public record EntitlementResult(decimal PayableAmount, string RuleVersion, string Basis);
