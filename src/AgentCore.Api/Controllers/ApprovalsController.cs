using System.Security.Claims;
using AgentCore.Api.Contracts;
using AgentCore.Application.Approvals;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

[ApiController]
[Route("api/approvals")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class ApprovalsController : ControllerBase
{
    private readonly ApprovalService _approvals;

    public ApprovalsController(ApprovalService approvals) => _approvals = approvals;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PendingActionDto>>> GetByStatus(
        [FromQuery] string status = "AwaitingApproval", CancellationToken ct = default)
    {
        if (!Enum.TryParse<PendingActionStatus>(status, ignoreCase: true, out var parsed))
        {
            return BadRequest($"Invalid status '{status}'. Expected one of: {string.Join(", ", Enum.GetNames<PendingActionStatus>())}.");
        }

        var actions = await _approvals.GetByStatusAsync(parsed, ct);
        return Ok(actions.Select(ToDto));
    }

    [HttpPost("{id:int}/approve")]
    public async Task<ActionResult<PendingActionDto>> Approve(int id, CancellationToken ct)
    {
        var result = await _approvals.ApproveAsync(id, DecidedByRole(), DecidedByName(), ct);
        return ToActionResult(id, result);
    }

    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<PendingActionDto>> Reject(int id, CancellationToken ct)
    {
        var result = await _approvals.RejectAsync(id, DecidedByRole(), ct);
        return ToActionResult(id, result);
    }

    /// <summary>Batch approval/rejection (docs/plan.md section 13, "HITL gaps"): one decision per
    /// id, applied in order, with full per-action detail in the response - never a single
    /// generic "N actions processed" confirmation.</summary>
    [HttpPost("batch")]
    public async Task<ActionResult<IReadOnlyList<ApprovalBatchItemResult>>> DecideBatch(
        ApprovalBatchRequest request, CancellationToken ct)
    {
        if (request.Decisions.Count == 0)
        {
            return BadRequest("At least one decision is required.");
        }

        var decisions = request.Decisions.Select(d => (d.Id, d.Approve)).ToList();
        var results = await _approvals.DecideBatchAsync(decisions, DecidedByRole(), DecidedByName(), ct);

        return Ok(results.Select(r => new ApprovalBatchItemResult(
            r.Id,
            r.Result.Outcome.ToString(),
            r.Result.Action is null ? null : ToDto(r.Result.Action),
            r.Result.Outcome switch
            {
                ApprovalOutcome.NotFound => $"No pending action found with Id {r.Id}.",
                ApprovalOutcome.AlreadyDecided => $"Pending action {r.Id} has already been decided (status: {r.Result.Action!.Status}).",
                ApprovalOutcome.Expired => $"Pending action {r.Id} expired at {r.Result.Action!.ExpiresAt:O} and can no longer be approved.",
                _ => r.Result.Action!.ExecutionError
            })).ToList());
    }

    private ActionResult<PendingActionDto> ToActionResult(int id, ApprovalResult result) => result.Outcome switch
    {
        ApprovalOutcome.NotFound => NotFound($"No pending action found with Id {id}."),
        ApprovalOutcome.AlreadyDecided => Conflict($"Pending action {id} has already been decided (status: {result.Action!.Status})."),
        ApprovalOutcome.Expired => StatusCode(410, $"Pending action {id} expired at {result.Action!.ExpiresAt:O} and can no longer be approved; reject it instead."),
        _ => Ok(ToDto(result.Action!))
    };

    private string DecidedByRole() => User.FindFirstValue(ClaimTypes.Role) ?? Roles.Admin;
    private string DecidedByName() => User.Identity?.Name ?? "Unknown";

    private static PendingActionDto ToDto(PendingAction a) => new(
        a.Id, a.ClaimId, a.ActionType.ToString(), a.Payload, a.Status.ToString(),
        a.RequestedAtUtc, a.DecidedByRole, a.DecidedAtUtc,
        a.ExpiresAt, a.Status == PendingActionStatus.AwaitingApproval && a.ExpiresAt < DateTime.UtcNow, a.ExecutionError,
        a.RuleOutputsJson);
}
