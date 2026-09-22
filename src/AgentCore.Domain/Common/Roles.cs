namespace AgentCore.Domain.Common;

/// <summary>The three roles AgentCore recognizes, strictly nested: SuperAdmin ⊃ Admin ⊃
/// CaseManager (docs/plan.md section 5). Asserted via a JWT's role claims, issued at login -
/// a SuperAdmin's token carries all three role claims, an Admin's carries Admin+CaseManager, a
/// CaseManager's carries just CaseManager, so every `[Authorize(Roles = ...)]` attribute needs
/// only the literal role names it lists, never a separate SuperAdmin-specific check.</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string CaseManager = "CaseManager";

    public static readonly string[] All = [SuperAdmin, Admin, CaseManager];
}
