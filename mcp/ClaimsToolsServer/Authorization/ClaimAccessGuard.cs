using AgentCore.Domain.Authorization;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;

namespace ClaimsToolsServer.Authorization;

/// <summary>Shared "resolve claim -> worker -> check permission" step for every tool keyed by
/// claimId (docs/plan.md section 5) - avoids duplicating the same three-step lookup across
/// CheckCoverageTool/EvaluateEscalationTool/ScoreClaimRiskTool/CalculatePayoutTool/
/// SendWorkerEmailTool/SendEscalationEmailTool.</summary>
public static class ClaimAccessGuard
{
    public static async Task<(Claim? Claim, Worker? Worker, string? DenialMessage)> ResolveAndCheckAsync(
        IClaimRepository claims, IWorkerRepository workers, int claimId, CallerContext caller)
    {
        var claim = await claims.GetByIdAsync(claimId);
        if (claim is null)
        {
            return (null, null, $"No claim found with Id {claimId}.");
        }

        var worker = await workers.GetByIdAsync(claim.WorkerId);
        if (worker is null || !WorkerAccessPolicy.CanAccessWorker(worker, caller.CallerRoles, caller.UserId))
        {
            return (claim, worker, $"You do not have permission to access claim #{claimId} - its worker is not assigned to you.");
        }

        return (claim, worker, null);
    }
}
