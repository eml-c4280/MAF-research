using System.Text.Json;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Notifications;
using AgentCore.Domain.Repositories;

namespace AgentCore.Application.Approvals;

public class ApprovalService
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly IEmailSender _emailSender;

    public ApprovalService(
        IPendingActionRepository pendingActions,
        IClaimRepository claims,
        IWorkerRepository workers,
        IEmailSender emailSender)
    {
        _pendingActions = pendingActions;
        _claims = claims;
        _workers = workers;
        _emailSender = emailSender;
    }

    public Task<IReadOnlyList<PendingAction>> GetByStatusAsync(PendingActionStatus status, CancellationToken ct = default) =>
        _pendingActions.GetByStatusAsync(status, ct);

    public async Task<ApprovalResult> ApproveAsync(int id, string decidedByRole, string decidedByName, CancellationToken ct = default)
    {
        var action = await _pendingActions.GetByIdAsync(id, ct);
        if (action is null)
        {
            return new ApprovalResult(ApprovalOutcome.NotFound, null);
        }

        if (action.Status != PendingActionStatus.AwaitingApproval)
        {
            return new ApprovalResult(ApprovalOutcome.AlreadyDecided, action);
        }

        if (action.ExpiresAt < DateTime.UtcNow)
        {
            return new ApprovalResult(ApprovalOutcome.Expired, action);
        }

        // The side effect (email send, payout write-back) can still throw here even though the
        // approval decision itself is valid - e.g. the claim was deleted in the meantime, or the
        // notification port fails. That must not surface as an unhandled 500: the decision to
        // approve stands, but the execution outcome becomes a visible, queryable status instead
        // of a stack trace - see docs/plan.md section 13 ("HITL gaps").
        try
        {
            await ExecuteAsync(action, decidedByRole, decidedByName, ct);
            action.Status = PendingActionStatus.Executed;
            action.ExecutionError = null;
        }
        catch (Exception ex)
        {
            action.Status = PendingActionStatus.ExecutionFailed;
            action.ExecutionError = ex.Message;
        }

        action.DecidedByRole = decidedByRole;
        action.DecidedAtUtc = DateTime.UtcNow;
        await _pendingActions.UpdateAsync(action, ct);

        return new ApprovalResult(ApprovalOutcome.Success, action);
    }

    public async Task<ApprovalResult> RejectAsync(int id, string decidedByRole, CancellationToken ct = default)
    {
        var action = await _pendingActions.GetByIdAsync(id, ct);
        if (action is null)
        {
            return new ApprovalResult(ApprovalOutcome.NotFound, null);
        }

        if (action.Status != PendingActionStatus.AwaitingApproval)
        {
            return new ApprovalResult(ApprovalOutcome.AlreadyDecided, action);
        }

        action.Status = PendingActionStatus.Rejected;
        action.DecidedByRole = decidedByRole;
        action.DecidedAtUtc = DateTime.UtcNow;
        await _pendingActions.UpdateAsync(action, ct);

        return new ApprovalResult(ApprovalOutcome.Success, action);
    }

    /// <summary>Batch approval (docs/plan.md section 13, "HITL gaps"): applies one decision per
    /// id, in order, and returns full per-action detail for every one - never a single generic
    /// confirmation, since collapsing a batch view obscures which specific action did what.</summary>
    public async Task<IReadOnlyList<(int Id, ApprovalResult Result)>> DecideBatchAsync(
        IReadOnlyList<(int Id, bool Approve)> decisions, string decidedByRole, string decidedByName, CancellationToken ct = default)
    {
        var results = new List<(int Id, ApprovalResult Result)>(decisions.Count);
        foreach (var (id, approve) in decisions)
        {
            var result = approve
                ? await ApproveAsync(id, decidedByRole, decidedByName, ct)
                : await RejectAsync(id, decidedByRole, ct);
            results.Add((id, result));
        }

        return results;
    }

    private async Task ExecuteAsync(PendingAction action, string decidedByRole, string decidedByName, CancellationToken ct)
    {
        switch (action.ActionType)
        {
            case PendingActionType.SendWorkerEmail:
                await ExecuteSendWorkerEmailAsync(action, ct);
                break;

            case PendingActionType.SendEscalationEmail:
                await ExecuteSendEscalationEmailAsync(action, ct);
                break;

            case PendingActionType.CalculatePayout:
                await ExecuteCalculatePayoutAsync(action, decidedByRole, decidedByName, ct);
                break;

            case PendingActionType.NotifyCaseManager:
                await ExecuteNotifyCaseManagerAsync(action, ct);
                break;

            default:
                throw new NotSupportedException($"Unknown PendingActionType '{action.ActionType}'.");
        }
    }

    private async Task ExecuteSendWorkerEmailAsync(PendingAction action, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmailPayload>(action.Payload, PayloadOptions)
            ?? throw new InvalidOperationException($"PendingAction {action.Id} has no readable email payload.");

        var claim = await _claims.GetByIdAsync(action.ClaimId, ct)
            ?? throw new InvalidOperationException($"Claim {action.ClaimId} for PendingAction {action.Id} no longer exists.");
        var worker = await _workers.GetByIdAsync(claim.WorkerId, ct)
            ?? throw new InvalidOperationException($"Worker {claim.WorkerId} for claim {claim.Id} no longer exists.");

        await _emailSender.SendAsync(worker.Email, payload.Subject, payload.Body, ct);
    }

    private async Task ExecuteSendEscalationEmailAsync(PendingAction action, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EscalationPayload>(action.Payload, PayloadOptions)
            ?? throw new InvalidOperationException($"PendingAction {action.Id} has no readable escalation payload.");

        const string escalationRecipient = "escalations@agentcore.local";
        var subject = $"[Escalation] Claim #{action.ClaimId}: {payload.Reason}";
        await _emailSender.SendAsync(escalationRecipient, subject, payload.Body, ct);
    }

    private async Task ExecuteCalculatePayoutAsync(PendingAction action, string decidedByRole, string decidedByName, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PayoutPayload>(action.Payload, PayloadOptions)
            ?? throw new InvalidOperationException($"PendingAction {action.Id} has no readable payout payload.");

        var claim = await _claims.GetByIdAsync(action.ClaimId, ct)
            ?? throw new InvalidOperationException($"Claim {action.ClaimId} for PendingAction {action.Id} no longer exists.");

        claim.Amount = payload.ProposedAmount;
        claim.Status = ClaimStatus.Approved;
        claim.ReviewedBy = $"{decidedByRole} ({decidedByName})";
        claim.ReviewedAtUtc = DateTime.UtcNow;
        await _claims.UpdateAsync(claim, ct);
    }

    private async Task ExecuteNotifyCaseManagerAsync(PendingAction action, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CaseManagerNotificationPayload>(action.Payload, PayloadOptions)
            ?? throw new InvalidOperationException($"PendingAction {action.Id} has no readable case-manager notification payload.");

        await _emailSender.SendAsync(payload.RecipientEmail, payload.Subject, payload.Body, ct);
    }

    private record EmailPayload(string Subject, string Body);
    private record EscalationPayload(string Reason, string Body);
    private record PayoutPayload(decimal ProposedAmount, string Justification);
    private record CaseManagerNotificationPayload(string RecipientEmail, string Subject, string Body);
}
