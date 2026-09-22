namespace AgentCore.Domain.Entities;

public class Worker
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public int YearsOfExperience { get; set; }
    public bool IsAvailable { get; set; }

    /// <summary>The row-level permission boundary a CaseManager is scoped by (docs/plan.md
    /// section 5) - null means unassigned, which denies every CaseManager by default (deny, not
    /// allow, when assignment is absent). Only Admin/SuperAdmin can set this.</summary>
    public int? AssignedCaseManagerUserId { get; set; }
    public User? AssignedCaseManager { get; set; }

    public ICollection<InsurancePolicy> Policies { get; set; } = new List<InsurancePolicy>();
    public ICollection<Claim> Claims { get; set; } = new List<Claim>();
}
