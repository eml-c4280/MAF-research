using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Authorization;

/// <summary>
/// Row-level access rule for a Worker (docs/plan.md section 5, docs/knowledge-base.md Topic 4):
/// a CaseManager may only access a worker assigned to them; Admin/SuperAdmin are unrestricted.
/// Pure and dependency-free (same placement rationale as Phase 10's rule engines in
/// AgentCore.Domain.Rules / AgentCoreDiagnostics) so both AgentCore.Api and mcp/ClaimsToolsServer
/// - a separate process - can enforce the identical rule without a new cross-project reference.
/// </summary>
public static class WorkerAccessPolicy
{
    public static bool CanAccessWorker(Worker worker, IEnumerable<string> callerRoles, int? callerUserId)
    {
        var roles = callerRoles as ICollection<string> ?? callerRoles.ToList();
        if (roles.Contains(Roles.Admin))
        {
            // Covers SuperAdmin too: a SuperAdmin's role set always includes "Admin" (issued as
            // an inherited claim at login) - see docs/plan.md section 5.
            return true;
        }

        return callerUserId.HasValue && worker.AssignedCaseManagerUserId == callerUserId.Value;
    }
}
