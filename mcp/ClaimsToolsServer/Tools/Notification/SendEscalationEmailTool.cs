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
/// Sensitive tool: never sends anything itself. It only records the proposed escalation as a
/// PendingAction awaiting Admin/CaseManager approval. ProposedByAgentRunId is left unset here and
/// backfilled by the host after observing this tool's result - see docs/plan-mcp.md section 4.
/// Row-level scoped (docs/plan.md section 5): denies a CaseManager caller a claim whose worker is
/// not assigned to them.
/// </summary>
[McpServerToolType]
public class SendEscalationEmailTool
{
    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly CallerContext _caller;

    public SendEscalationEmailTool(IPendingActionRepository pendingActions, IClaimRepository claims, IWorkerRepository workers, CallerContext caller)
    {
        _pendingActions = pendingActions;
        _claims = claims;
        _workers = workers;
        _caller = caller;
    }

    [McpServerTool(Name = "EscalationEmailSender", Destructive = true)]
    [Description("Propose escalating this claim to a higher level (e.g. a supervisor or claims manager) by email — distinct from WorkerEmailSender, which emails the worker directly, not a supervisor. This does NOT send the email — it queues it for Admin/CaseManager approval first.")]
    public async Task<PendingActionRef> SendEscalationEmailAsync(
        [Description("The Id of the claim being escalated.")] int claimId,
        [Description("Why this claim needs escalation (e.g. high amount, suspected fraud, missing documentation).")] string reason,
        [Description("Body text of the escalation email.")] string body)
    {
        var (_, _, denial) = await ClaimAccessGuard.ResolveAndCheckAsync(_claims, _workers, claimId, _caller);
        if (denial is not null)
        {
            return new PendingActionRef(0, nameof(PendingActionType.SendEscalationEmail), denial);
        }

        var idempotencyKey = PendingActionIdempotency.ComputeKey(claimId, nameof(PendingActionType.SendEscalationEmail));
        var existing = await _pendingActions.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existing is not null)
        {
            return new PendingActionRef(existing.Id, nameof(PendingActionType.SendEscalationEmail));
        }

        var action = new PendingAction
        {
            ClaimId = claimId,
            ActionType = PendingActionType.SendEscalationEmail,
            Payload = JsonSerializer.Serialize(new { reason, body }),
            Status = PendingActionStatus.AwaitingApproval,
            IdempotencyKey = idempotencyKey
        };

        await _pendingActions.AddAsync(action);
        AgentCoreDiagnostics.PendingActionsQueued.Add(1, new TagList { { "action_type", nameof(PendingActionType.SendEscalationEmail) } });
        return new PendingActionRef(action.Id, nameof(PendingActionType.SendEscalationEmail));
    }
}
