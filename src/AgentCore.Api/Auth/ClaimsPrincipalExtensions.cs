using System.Security.Claims;
using AgentCore.Agents;

namespace AgentCore.Api.Auth;

/// <summary>Turns the validated JWT's claims into the CallerIdentity every agent/MCP call now
/// requires (docs/plan.md section 5) - one place so every controller extracts it the same way.</summary>
public static class ClaimsPrincipalExtensions
{
    public static CallerIdentity ToCallerIdentity(this ClaimsPrincipal user)
    {
        var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        return new CallerIdentity(userId, roles);
    }
}
