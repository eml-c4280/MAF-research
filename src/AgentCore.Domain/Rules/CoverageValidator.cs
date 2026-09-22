using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Rules;

/// <summary>
/// CV — Coverage validation (docs/business-logic.md §4, owner: Code). Deterministic: the agent
/// may call this (via the ClaimsToolsServer "CoverageChecker" tool) and must quote the result, but
/// cannot override it - a coverage failure is rule-decided and terminal (ClaimStatus.CoverageRejected),
/// no agent judgement involved.
///
/// Deliberately not implemented: CV-5 (ambiguity across multiple overlapping policies) -
/// IInsurancePolicyRepository.GetByWorkerIdAsync returns a single policy, matching this project's
/// 1-worker-to-1-policy seed data; a real multi-policy ambiguity check would need that repository
/// contract to change first, which is out of scope for this pass.
/// </summary>
public static class CoverageValidator
{
    // "Illustrative demo defaults" per docs/business-logic.md's own caution - not drawn from any
    // real scheme's coverage-type taxonomy.
    private static readonly Dictionary<string, string[]> CoveredClaimTypesByPolicyType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Workers Compensation"] = ["Injury", "Medical"],
        ["Public Liability"] = ["Liability", "Property Damage"],
    };

    public static CoverageValidationResult Validate(Claim claim, InsurancePolicy? policy, IReadOnlyList<Claim> workerClaimHistory)
    {
        var details = new List<string>();

        if (policy is null)
        {
            details.Add("CV-1: no policy on file for this worker.");
            return new CoverageValidationResult(false, "No policy on file for this worker.", details, null);
        }

        // CV-1: incident must fall within the policy's in-force period.
        var withinPeriod = claim.IncidentDate >= policy.StartDate && claim.IncidentDate <= policy.EndDate;
        details.Add(withinPeriod
            ? $"CV-1 pass: incident date {claim.IncidentDate:yyyy-MM-dd} falls within policy {policy.PolicyNumber}'s period ({policy.StartDate:yyyy-MM-dd} to {policy.EndDate:yyyy-MM-dd})."
            : $"CV-1 FAIL: incident date {claim.IncidentDate:yyyy-MM-dd} falls outside policy {policy.PolicyNumber}'s period ({policy.StartDate:yyyy-MM-dd} to {policy.EndDate:yyyy-MM-dd}).");

        // CV-2: claim type must be one the policy's coverage type actually covers.
        var coveredTypes = CoveredClaimTypesByPolicyType.GetValueOrDefault(policy.CoverageType, []);
        var typeCovered = coveredTypes.Contains(claim.ClaimType, StringComparer.OrdinalIgnoreCase);
        details.Add(typeCovered
            ? $"CV-2 pass: claim type '{claim.ClaimType}' is covered by policy type '{policy.CoverageType}'."
            : $"CV-2 FAIL: claim type '{claim.ClaimType}' is not covered by policy type '{policy.CoverageType}' (covers: {string.Join(", ", coveredTypes)}).");

        // CV-3: cumulative claims (including this one) against the policy must not exceed cover.
        var cumulative = workerClaimHistory.Where(c => c.Id != claim.Id).Sum(c => c.Amount) + claim.Amount;
        var withinCoverAmount = cumulative <= policy.CoverageAmount;
        decimal? cappedAmount = null;
        if (!withinCoverAmount)
        {
            cappedAmount = Math.Max(0, policy.CoverageAmount - (cumulative - claim.Amount));
            details.Add($"CV-3 FAIL: cumulative claims against policy {policy.PolicyNumber} ({cumulative:C}) exceed cover ({policy.CoverageAmount:C}); remaining cover for this claim is {cappedAmount:C}.");
        }
        else
        {
            details.Add($"CV-3 pass: cumulative claims against policy {policy.PolicyNumber} ({cumulative:C}) are within cover ({policy.CoverageAmount:C}).");
        }

        // CV-4: IsActive should agree with EndDate vs today - a data-integrity flag, not a blocker.
        var expectedActive = policy.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow);
        if (policy.IsActive != expectedActive)
        {
            details.Add($"CV-4 FLAG: policy {policy.PolicyNumber}.IsActive={policy.IsActive} disagrees with its EndDate ({policy.EndDate:yyyy-MM-dd}) relative to today - data-integrity issue, not auto-resolved.");
        }

        var passed = withinPeriod && typeCovered;
        var rejectionReason = passed ? null : !withinPeriod && !typeCovered
            ? "Outside the policy's coverage period, and the claim type isn't covered by the policy type (CV-1 and CV-2)."
            : !withinPeriod
                ? "Outside the policy's coverage period (CV-1)."
                : "Claim type isn't covered by the policy type (CV-2).";

        return new CoverageValidationResult(passed, rejectionReason, details, passed ? cappedAmount : null);
    }
}

/// <param name="Passed">False means CV-1 and/or CV-2 failed - rule-decided CoverageRejected, terminal.</param>
/// <param name="RejectionReason">Human-readable reason when Passed is false; null otherwise.</param>
/// <param name="Details">Every sub-rule's verdict (CV-1 through CV-4), for full transparency to the approver.</param>
/// <param name="CappedAmount">Set only when CV-3 caps the payable amount below the claimed Amount.</param>
public record CoverageValidationResult(bool Passed, string? RejectionReason, IReadOnlyList<string> Details, decimal? CappedAmount);
