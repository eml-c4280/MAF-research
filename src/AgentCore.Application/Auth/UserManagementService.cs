using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Identity;

namespace AgentCore.Application.Auth;

public record UserCreationResult(bool Success, string? Error, User? User)
{
    public static UserCreationResult Denied(string error) => new(false, error, null);
    public static UserCreationResult CreatedResult(User user) => new(true, null, user);
}

/// <summary>
/// The "who can create/manage whom" rule from docs/plan.md section 5 lives here as real,
/// enforced logic - not left to controller-level [Authorize] alone, since [Authorize] can only
/// gate "is this caller an Admin at all", not "which specific role is an Admin allowed to hand
/// out".
/// </summary>
public class UserManagementService
{
    private readonly IUserRepository _users;
    private readonly PasswordHasher<User> _hasher = new();

    public UserManagementService(IUserRepository users) => _users = users;

    public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default) => _users.GetAllAsync(ct);

    public async Task<UserCreationResult> CreateUserAsync(
        UserRole creatorRole, int creatorUserId, string name, string email, string password, UserRole requestedRole,
        CancellationToken ct = default)
    {
        if (!CanAssign(creatorRole, requestedRole))
        {
            return UserCreationResult.Denied($"A {creatorRole} cannot create a {requestedRole} account.");
        }

        if (await _users.GetByEmailAsync(email, ct) is not null)
        {
            return UserCreationResult.Denied($"A user with email '{email}' already exists.");
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Role = requestedRole,
            CreatedByUserId = creatorUserId
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        await _users.AddAsync(user, ct);
        return UserCreationResult.CreatedResult(user);
    }

    public async Task<UserCreationResult> UpdateAsync(int userId, string name, string email, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return UserCreationResult.Denied($"No user found with Id {userId}.");
        }

        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null && existing.Id != userId)
        {
            return UserCreationResult.Denied($"A user with email '{email}' already exists.");
        }

        user.Name = name;
        user.Email = email;
        await _users.UpdateAsync(user, ct);
        return UserCreationResult.CreatedResult(user);
    }

    public async Task<bool> DeactivateAsync(int userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        user.IsActive = false;
        await _users.UpdateAsync(user, ct);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        await _users.UpdateAsync(user, ct);
        return true;
    }

    /// <summary>SuperAdmin -> any role. Admin -> CaseManager only (not Admin/SuperAdmin, so a
    /// compromised Admin account can't mint itself more power). CaseManager -> nothing.</summary>
    private static bool CanAssign(UserRole creatorRole, UserRole requestedRole) => creatorRole switch
    {
        UserRole.SuperAdmin => true,
        UserRole.Admin => requestedRole == UserRole.CaseManager,
        _ => false
    };
}
