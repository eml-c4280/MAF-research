using AgentCore.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace ClaimsToolsServer.Authorization;

/// <summary>
/// The identity AgentCore.Api asserted for this MCP call (docs/plan.md section 5, OBO) - read
/// from two headers only the trusted API process sets (never the end caller, who never sees this
/// request at all). Scoped per-request via IHttpContextAccessor, same lifetime as the MCP tool
/// classes themselves.
/// </summary>
public class CallerContext
{
    public int? UserId { get; }
    public IReadOnlyCollection<string> CallerRoles { get; }

    public CallerContext(IHttpContextAccessor httpContextAccessor)
    {
        var headers = httpContextAccessor.HttpContext?.Request.Headers;

        UserId = headers is not null &&
                 headers.TryGetValue("X-Caller-User-Id", out var userIdValues) &&
                 int.TryParse(userIdValues.ToString(), out var userId)
            ? userId
            : null;

        CallerRoles = headers is not null && headers.TryGetValue("X-Caller-Role", out var roleValues)
            ? roleValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    public bool IsAdminOrAbove => CallerRoles.Contains(Roles.Admin);
}
