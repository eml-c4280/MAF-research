using System.IdentityModel.Tokens.Jwt;
using System.Text;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
// AgentCore.Domain.Entities.Claim (an insurance claim) collides with System.Security.Claims.Claim
// (a JWT claim) - aliased rather than fully-qualifying every use below.
using SecurityClaim = System.Security.Claims.Claim;
using ClaimTypes = System.Security.Claims.ClaimTypes;

namespace AgentCore.Application.Auth;

/// <summary>
/// Issues the JWT a user gets on login. The key mechanism decision (docs/plan.md section 5): role
/// claims include everything the holder's tier *inherits*, not just their own role - a SuperAdmin
/// token carries SuperAdmin+Admin+CaseManager, an Admin token carries Admin+CaseManager, a
/// CaseManager token carries just CaseManager. This means every existing
/// `[Authorize(Roles = "Admin,CaseManager")]`-style attribute needs no SuperAdmin-specific logic
/// anywhere - SuperAdmin already satisfies any check that accepts Admin.
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string AccessToken, DateTime ExpiresAtUtc) IssueToken(User user)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var claims = new List<SecurityClaim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
        };
        claims.AddRange(GetInheritedRoles(user.Role).Select(r => new SecurityClaim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims, expires: expiresAtUtc, signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public static IReadOnlyList<string> GetInheritedRoles(UserRole role) => role switch
    {
        UserRole.SuperAdmin => [Roles.SuperAdmin, Roles.Admin, Roles.CaseManager],
        UserRole.Admin => [Roles.Admin, Roles.CaseManager],
        UserRole.CaseManager => [Roles.CaseManager],
        _ => []
    };
}
