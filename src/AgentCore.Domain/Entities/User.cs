using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Entities;

/// <summary>
/// A real AgentCore staff/system account (docs/plan.md section 5) - NOT to be confused with
/// <see cref="Worker"/>, the injured employee a claim is about. Two completely separate concepts
/// that happen to both have an Email field.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>Soft-disable, never hard-delete - preserves the audit trail anything this user
    /// did (AgentRunLog.Trigger, PendingAction.DecidedByRole, etc.) still points to.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Who created this account - null only for the seeded SuperAdmin.</summary>
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
}
