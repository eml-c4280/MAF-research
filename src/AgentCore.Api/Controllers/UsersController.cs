using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Application.Auth;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

/// <summary>User management (docs/plan.md section 5) - Admin and above only. The actual
/// "who can create whom" hierarchy rule is enforced inside UserManagementService, not here;
/// [Authorize] only gates "is this caller an Admin at all".</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly UserManagementService _users;

    public UsersController(UserManagementService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        var users = await _users.ListUsersAsync(ct);
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var requestedRole))
        {
            return BadRequest($"Invalid role '{request.Role}'. Expected one of: {string.Join(", ", Enum.GetNames<UserRole>())}.");
        }

        var caller = User.ToCallerIdentity();
        var creatorRole = HighestRole(caller.Roles);

        var result = await _users.CreateUserAsync(creatorRole, caller.UserId, request.Name, request.Email, request.Password, requestedRole, ct);
        if (!result.Success)
        {
            return Conflict(result.Error);
        }

        return Ok(ToDto(result.User!));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest request, CancellationToken ct)
    {
        var result = await _users.UpdateAsync(id, request.Name, request.Email, ct);
        if (!result.Success)
        {
            return result.Error!.StartsWith("No user found") ? NotFound(result.Error) : Conflict(result.Error);
        }

        return Ok(ToDto(result.User!));
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var deactivated = await _users.DeactivateAsync(id, ct);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        var reset = await _users.ResetPasswordAsync(id, request.NewPassword, ct);
        return reset ? NoContent() : NotFound();
    }

    /// <summary>A caller's JWT carries every role its tier inherits (docs/plan.md section 5), so
    /// SuperAdmin/Admin/CaseManager may all be present at once - pick the highest for the
    /// "who can this caller assign" check in UserManagementService.</summary>
    private static UserRole HighestRole(IReadOnlyCollection<string> roles) =>
        roles.Contains(Roles.SuperAdmin) ? UserRole.SuperAdmin :
        roles.Contains(Roles.Admin) ? UserRole.Admin :
        UserRole.CaseManager;

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.Name, u.Role.ToString(), u.IsActive, u.CreatedAtUtc, u.LastLoginAtUtc);
}
