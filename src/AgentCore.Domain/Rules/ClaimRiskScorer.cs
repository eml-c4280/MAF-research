using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Rules;

/// <summary>
/// FR — Fraud and anomaly signals (docs/business-logic.md §4, owner: Agent flags, Human decides).
/// None of these auto-decline anything - each is a flag with evidence attached, for a human to
/// weigh, never a reason shown to the worker.
///
/// Only the signals computable from data this project actually has are implemented here (FR-1,
/// FR-4, FR-5). FR-3 (description inconsistent with the injury/role/mechanism) is deliberately
/// left to the agent's own reading of the text, per docs/business-logic.md §5 ("this is the
/// agent's strongest contribution; it reads text a rules engine can't") - it's not a code rule.
/// FR-2 (claim shortly after termination) and FR-6 (no witnesses/incident report) need fields
/// (employment end date, witness/report flags) this pass doesn't add.
/// </summary>
public static class ClaimRiskScorer
{
    // "Illustrative demo defaults" per docs/business-logic.md's own caution.
    private const int LongLagDays = 14;
    private const int ClusteringClaimCountTrigger = 3;

    public static ClaimRiskResult Score(Claim claim, IReadOnlyList<Claim> workerClaimHistory)
    {
        var flags = new List<RiskFlag>();

        // FR-1: long lag between the incident and it being reported.
        var lagDays = claim.ReportedDate.DayNumber - claim.IncidentDate.DayNumber;
        if (lagDays > LongLagDays)
        {
            flags.Add(new RiskFlag("FR-1", $"Incident on {claim.IncidentDate:yyyy-MM-dd}, not reported until {claim.ReportedDate:yyyy-MM-dd} - a {lagDays}-day gap with no explanation on file."));
        }

        // FR-4: repeated claims by the same worker (clustering).
        var totalClaims = workerClaimHistory.Count;
        if (totalClaims >= ClusteringClaimCountTrigger)
        {
            flags.Add(new RiskFlag("FR-4", $"This worker has {totalClaims} claims on file in total."));
        }

        // FR-5: incident on a Monday - weak signal alone, per docs/business-logic.md.
        if (claim.IncidentDate.DayOfWeek == DayOfWeek.Monday)
        {
            flags.Add(new RiskFlag("FR-5", $"Incident date {claim.IncidentDate:yyyy-MM-dd} is a Monday - weak signal alone, only meaningful combined with others."));
        }

        return new ClaimRiskResult(flags);
    }
}

public record RiskFlag(string RuleId, string Evidence);

public record ClaimRiskResult(IReadOnlyList<RiskFlag> Flags);
