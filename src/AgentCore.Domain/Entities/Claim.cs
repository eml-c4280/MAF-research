using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Entities;

public class Claim
{
    public int Id { get; set; }
    public int WorkerId { get; set; }
    public Worker? Worker { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public DateOnly ClaimDate { get; set; }
    public string ClaimType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Pending;
    public string Description { get; set; } = string.Empty;

    /// <summary>When the injury/incident actually happened - distinct from ClaimDate (lodgement).
    /// Drives CV-1 (coverage period) and FR-1 (incident-to-report lag). docs/business-logic.md §2.</summary>
    public DateOnly IncidentDate { get; set; }

    /// <summary>When the incident was first reported (to the employer/insurer) - distinct from
    /// both IncidentDate and ClaimDate. docs/business-logic.md §2.</summary>
    public DateOnly ReportedDate { get; set; }

    /// <summary>Australian state/territory code (NSW/VIC/QLD/WA/SA/…) the claim falls under -
    /// workers' comp entitlements are per-jurisdiction. docs/business-logic.md §2.</summary>
    public string Jurisdiction { get; set; } = string.Empty;

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? AgentRecommendation { get; set; }
    public double? AgentConfidence { get; set; }

    public ICollection<PendingAction> PendingActions { get; set; } = new List<PendingAction>();
}
