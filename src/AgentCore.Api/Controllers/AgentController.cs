using System.Security.Claims;
using AgentCore.Agents;
using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Application.Agents;
using AgentCore.Application.Workflows;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

[ApiController]
[Route("api/agent")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class AgentController : ControllerBase
{
    private readonly ClaimAgentService _agentService;
    private readonly WorkflowExecutionService _workflowExecutionService;

    public AgentController(ClaimAgentService agentService, WorkflowExecutionService workflowExecutionService)
    {
        _agentService = agentService;
        _workflowExecutionService = workflowExecutionService;
    }

    [HttpPost("query")]
    public async Task<ActionResult<AgentRunLogDto>> Query(AgentQueryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        // Alias for POST /api/agents/claims/query (Phase 13, docs/plan-agents.md §8) - kept for
        // backward compatibility now that free-form Q&A can target any specialist by name.
        var run = await _agentService.QueryAsync(AgentCatalog.ClaimsAgent, request.Prompt, DescribeTrigger("POST /api/agent/query"), User.ToCallerIdentity(), ct);
        return Ok(ToDto(run));
    }

    [HttpPost("claims/{id:int}/process")]
    public async Task<ActionResult<ProcessClaimResponse>> ProcessClaim(int id, CancellationToken ct)
    {
        // Phase 13 (docs/plan-agents.md §3 decision 2): runs the multi-agent "Process Claim"
        // workflow (Worker/Claims/Risk & Escalation/Notification specialists) instead of one
        // monolithic agent call - see WorkflowExecutionService.ProcessClaimAsync.
        var outcome = await _workflowExecutionService.ProcessClaimAsync(id, DescribeTrigger($"POST /api/agent/claims/{id}/process"), User.ToCallerIdentity(), ct);

        switch (outcome.Status)
        {
            case ClaimProcessingStatus.NotFound:
                return NotFound($"No claim found with Id {id}.");
            case ClaimProcessingStatus.Blocked:
                return Conflict(outcome.BlockReason);
            case ClaimProcessingStatus.Forbidden:
                return StatusCode(StatusCodes.Status403Forbidden, outcome.BlockReason);
        }

        var result = outcome.Result!;
        var response = new ProcessClaimResponse(
            ToDto(result.Run),
            result.Claim.Id,
            result.Run.FinalAnswer,
            result.Claim.Status.ToString(),
            result.QueuedActions.Select(ToDto).ToList(),
            result.Steps.Select(s => new AgentRunStepDto(s.AgentName, ToDto(s.Run))).ToList());

        return Ok(response);
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ConversationSessionDto>> StartSession(CancellationToken ct)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Unknown";
        var actor = User.Identity?.Name ?? "Unknown";
        var session = await _agentService.StartSessionAsync(role, actor, User.ToCallerIdentity(), ct);
        return Ok(ToDto(session));
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ConversationSessionDto>>> ListSessions(CancellationToken ct)
    {
        var sessions = await _agentService.ListSessionsAsync(ct);
        return Ok(sessions.Select(ToDto));
    }

    [HttpGet("sessions/{id:int}/messages")]
    public async Task<ActionResult<ConversationSessionMessagesResponse>> GetSessionMessages(int id, CancellationToken ct)
    {
        var result = await _agentService.GetSessionMessagesAsync(id, ct);
        if (result is null)
        {
            return NotFound($"No session found with Id {id}.");
        }

        return Ok(new ConversationSessionMessagesResponse(ToDto(result.Session), result.Turns.Select(ToDto).ToList()));
    }

    [HttpPost("sessions/{id:int}/messages")]
    public async Task<ActionResult<AgentRunLogDto>> SendSessionMessage(int id, SendSessionMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("Message is required.");
        }

        var result = await _agentService.ContinueSessionAsync(
            id, request.Message, DescribeTrigger($"POST /api/agent/sessions/{id}/messages"), User.ToCallerIdentity(), ct);
        if (result is null)
        {
            return NotFound($"No session found with Id {id}.");
        }

        return Ok(ToDto(result.Run));
    }

    private string DescribeTrigger(string endpoint)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Unknown";
        var actor = User.Identity?.Name ?? "Unknown";
        return $"{role} ({actor}) via {endpoint}";
    }

    private static AgentRunLogDto ToDto(AgentRunLog r) => new(
        r.Id, r.ClaimId, r.Trigger, r.Prompt, r.ToolCallsJson, r.FinalAnswer, r.ModelId, r.ToolCallCount,
        r.ReasoningText, r.InputTokenCount, r.OutputTokenCount, r.TotalTokenCount,
        r.InputCost, r.OutputCost, r.TotalCost, r.CreatedAtUtc, r.Outcome);

    private static ConversationSessionDto ToDto(ConversationSession s) => new(
        s.Id, s.Status.ToString(), s.Title, s.CreatedByRole, s.CreatedByName, s.CreatedAtUtc, s.LastActivityAtUtc);

    private static PendingActionDto ToDto(PendingAction a) => new(
        a.Id, a.ClaimId, a.ActionType.ToString(), a.Payload, a.Status.ToString(),
        a.RequestedAtUtc, a.DecidedByRole, a.DecidedAtUtc,
        a.ExpiresAt, a.Status == PendingActionStatus.AwaitingApproval && a.ExpiresAt < DateTime.UtcNow, a.ExecutionError,
        a.RuleOutputsJson);
}
