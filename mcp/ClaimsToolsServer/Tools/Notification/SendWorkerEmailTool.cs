using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AgentCore.Domain.Diagnostics;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>
/// Sensitive tool: never sends anything itself. It only records the proposed email as a
/// PendingAction awaiting Admin/CaseManager approval — the actual send happens when that approval
/// is granted. ProposedByAgentRunId is left unset here and backfilled by the host (AgentCore's
/// ClaimAgentService) after observing this tool's result - see docs/plan-mcp.md section 4.
/// Row-level scoped (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is
/// not assigned to them - a CaseManager must not be able to queue an email about a worker they
/// have no authority over, even though the send itself still awaits a human's approval.
/// </summary>
[McpServerToolType]
public class SendWorkerEmailTool
{
    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public SendWorkerEmailTool(IPendingActionRepository pendingActions, IClaimRepository claims, IWorkerRepository workers, CallerContext caller)
    {
        _pendingActions = pendingActions;
        _claims = claims;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "WorkerEmailSender", Destructive = true)]
    [Description("Propose sending an email to the worker about their claim. This does NOT send the email — it queues it for Admin/CaseManager approval first.")]
    public async Task<PendingActionRef> SendWorkerEmailAsync(
        [Description("The Id of the claim this email relates to.")] int claimId,
        [Description("Short subject line for the email.")] string subject,
        [Description("Body text of the email to send to the worker.")] string body)
    {
        var (_, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return new PendingActionRef(0, nameof(PendingActionType.SendWorkerEmail), denial);
        }

        var idempotencyKey = PendingActionIdempotency.ComputeKey(claimId, nameof(PendingActionType.SendWorkerEmail));
        var existing = await _pendingActions.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existing is not null)
        {
            return new PendingActionRef(existing.Id, nameof(PendingActionType.SendWorkerEmail));
        }

        var action = new PendingAction
        {
            ClaimId = claimId,
            ActionType = PendingActionType.SendWorkerEmail,
            Payload = JsonSerializer.Serialize(new { subject, body }),
            Status = PendingActionStatus.AwaitingApproval,
            IdempotencyKey = idempotencyKey
        };

        await _pendingActions.AddAsync(action);
        AgentCoreDiagnostics.PendingActionsQueued.Add(1, new TagList { { "action_type", nameof(PendingActionType.SendWorkerEmail) } });
        return new PendingActionRef(action.Id, nameof(PendingActionType.SendWorkerEmail));
    }
}
