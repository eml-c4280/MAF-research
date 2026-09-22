using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Application.Auth;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

/// <summary>JWT login/identity check (docs/plan.md section 5) - replaces the old X-Role header
/// trust model entirely.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IUserRepository _users;

    public AuthController(AuthService auth, IUserRepository users)
    {
        _auth = auth;
        _users = users;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request.Email, request.Password, ct);
        if (result is null)
        {
            return Unauthorized("Invalid email or password.");
        }

        return Ok(new LoginResponse(result.AccessToken, result.ExpiresAtUtc, ToDto(result.User)));
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var userId = User.ToCallerIdentity().UserId;
        var user = await _users.GetByIdAsync(userId, ct);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.Name, u.Role.ToString(), u.IsActive, u.CreatedAtUtc, u.LastLoginAtUtc);
}
