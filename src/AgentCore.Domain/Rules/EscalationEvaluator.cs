using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Rules;

/// <summary>
/// ES — Escalation triggers (docs/business-logic.md §4, owner: Code). Any trigger routes the
/// claim to a human tier; docs/business-logic.md §5 says a triggered escalation should also
/// block the agent's other sensitive-tool calls for that run (see ClaimAgentService).
///
/// Deliberately not implemented: ES-2 (fatality/permanent impairment/hospitalisation - no
/// severity field exists on Claim) and the "legal representation appointed" half of ES-3, and
/// ES-4 (time off work threshold - no work-capacity/return-to-work tracking exists). All three
/// need schema this pass doesn't add.
/// </summary>
public static class EscalationEvaluator
{
    // "Illustrative demo defaults" per docs/business-logic.md's own caution.
    private const decimal ManagerTierThreshold = 10_000m;
    private const decimal SeniorTierThreshold = 50_000m;
    private const int RepeatClaimWindowDays = 365;
    private const int RepeatClaimCountTrigger = 3;

    public static EscalationResult Evaluate(Claim claim, IReadOnlyList<Claim> workerClaimHistory, CoverageValidationResult coverage)
    {
        var triggered = new List<string>();
        string? tier = null;

        // ES-1: amount threshold.
        if (claim.Amount > SeniorTierThreshold)
        {
            triggered.Add("ES-1");
            tier = "Senior";
        }
        else if (claim.Amount > ManagerTierThreshold)
        {
            triggered.Add("ES-1");
            tier = "Manager";
        }

        // ES-3: already disputed.
        if (claim.Status == ClaimStatus.Disputed)
        {
            triggered.Add("ES-3");
            tier ??= "Manager";
        }

        // ES-5: third (or later) claim by the same worker within the trailing window.
        var windowStart = claim.ClaimDate.AddDays(-RepeatClaimWindowDays);
        var claimsInWindow = workerClaimHistory.Count(c => c.ClaimDate >= windowStart && c.ClaimDate <= claim.ClaimDate);
        if (claimsInWindow >= RepeatClaimCountTrigger)
        {
            triggered.Add("ES-5");
            tier ??= "Manager";
        }

        // ES-6: coverage failed on a claim that's already Approved (money paid) - recovery, not decline.
        if (!coverage.Passed && claim.Status == ClaimStatus.Approved)
        {
            triggered.Add("ES-6");
            tier = "Senior"; // a recovery case always goes to the highest tier regardless of amount.
        }

        return new EscalationResult(triggered.Count > 0, triggered, tier);
    }
}

/// <param name="Triggered">True if any ES rule fired.</param>
/// <param name="TriggeredRuleIds">Which specific ES rule(s) fired, e.g. ["ES-1", "ES-5"].</param>
/// <param name="Tier">"Manager" or "Senior" - null if nothing triggered.</param>
public record EscalationResult(bool Triggered, IReadOnlyList<string> TriggeredRuleIds, string? Tier);
