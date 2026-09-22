using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Identity;

namespace AgentCore.Application.Auth;

public record AuthResult(string AccessToken, DateTime ExpiresAtUtc, User User);

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly JwtTokenService _tokens;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthService(IUserRepository users, JwtTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    /// <summary>Null on any failure (unknown email, wrong password, deactivated account) -
    /// deliberately not distinguishing which, so a login attempt can't be used to enumerate
    /// valid emails.</summary>
    public async Task<AuthResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _users.UpdateAsync(user, ct);

        var (accessToken, expiresAtUtc) = _tokens.IssueToken(user);
        return new AuthResult(accessToken, expiresAtUtc, user);
    }
}
