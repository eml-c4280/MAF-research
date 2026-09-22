namespace AgentCore.Domain.Enums;

/// <summary>Strictly nested: SuperAdmin ⊃ Admin ⊃ CaseManager (docs/plan.md section 5) - each
/// higher role can do everything a lower one can, plus more.</summary>
public enum UserRole
{
    CaseManager,
    Admin,
    SuperAdmin
}
