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
/// Sensitive tool (Phase 13, docs/plan-agents.md): notifies a claim's worker's own assigned
/// CaseManager with a status update - distinct from SendEscalationEmailTool, which goes to a
/// supervisor/claims manager for risk review, not the worker's day-to-day case manager. Never
/// sends anything itself - only records a PendingAction awaiting Admin/CaseManager approval, same
/// as every other sensitive tool. Owned exclusively by NotificationAgent. Row-level scoped
/// (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is not assigned to
/// them, and separately reports (not a permission denial - nothing to notify) if the worker has no
/// assigned CaseManager at all.
/// </summary>
[McpServerToolType]
public class NotifyCaseManagerTool
{
    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly IUserRepository _users;
    private readonly CallerContext _caller;

    public NotifyCaseManagerTool(
        IPendingActionRepository pendingActions, IClaimRepository claims, IWorkerRepository workers,
        IUserRepository users, CallerContext caller)
    {
        _pendingActions = pendingActions;
        _claims = claims;
        _workers = workers;
        _users = users;
        _caller = caller;
    }

    [McpServerTool(Name = "CaseManagerNotifier", Destructive = true)]
    [Description("Propose notifying the worker's own assigned case manager with a status update about their claim — distinct from EscalationEmailSender, which goes to a supervisor for risk review, not the worker's day-to-day case manager. This does NOT send the email — it queues it for Admin/CaseManager approval first. Reports back (no approval queued) if the worker has no assigned case manager.")]
    public async Task<PendingActionRef> NotifyCaseManagerAsync(
        [Description("The Id of the claim this update relates to.")] int claimId,
        [Description("Short subject line for the email.")] string subject,
        [Description("Body text summarizing the claim decision for the case manager.")] string body)
    {
        var (claim, worker, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return new PendingActionRef(0, nameof(PendingActionType.NotifyCaseManager), denial);
        }

        if (worker!.AssignedCaseManagerUserId is not int caseManagerUserId)
        {
            return new PendingActionRef(0, nameof(PendingActionType.NotifyCaseManager),
                $"Worker {worker.Code} has no assigned case manager - nothing to notify.");
        }

        var caseManager = await _users.GetByIdAsync(caseManagerUserId);
        if (caseManager is null || !caseManager.IsActive)
        {
            return new PendingActionRef(0, nameof(PendingActionType.NotifyCaseManager),
                $"Worker {worker.Code}'s assigned case manager account no longer exists or is deactivated.");
        }

        var idempotencyKey = PendingActionIdempotency.ComputeKey(claimId, nameof(PendingActionType.NotifyCaseManager));
        var existing = await _pendingActions.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existing is not null)
        {
            return new PendingActionRef(existing.Id, nameof(PendingActionType.NotifyCaseManager));
        }

        var action = new PendingAction
        {
            ClaimId = claimId,
            ActionType = PendingActionType.NotifyCaseManager,
            Payload = JsonSerializer.Serialize(new { recipientEmail = caseManager.Email, subject, body }),
            Status = PendingActionStatus.AwaitingApproval,
            IdempotencyKey = idempotencyKey
        };

        await _pendingActions.AddAsync(action);
        AgentCoreDiagnostics.PendingActionsQueued.Add(1, new TagList { { "action_type", nameof(PendingActionType.NotifyCaseManager) } });
        return new PendingActionRef(action.Id, nameof(PendingActionType.NotifyCaseManager));
    }
}
