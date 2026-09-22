using System.Security.Claims;
using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Application.Workflows;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

/// <summary>Named, parameterized playbooks (docs/plan.md §11) - not a general workflow builder.
/// A structured trigger fills a fixed template's inputs directly; a chat trigger routes free text
/// to the best-matching workflow, falling back to /api/agent/query when nothing matches
/// confidently.</summary>
[ApiController]
[Route("api/workflows")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowExecutionService _workflows;

    public WorkflowsController(WorkflowExecutionService workflows) => _workflows = workflows;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowDefinitionDto>>> ListDefinitions(CancellationToken ct)
    {
        var definitions = await _workflows.ListDefinitionsAsync(ct);
        return Ok(definitions.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<WorkflowDefinitionDto>> CreateDefinition(CreateWorkflowRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var definition = new WorkflowDefinition
        {
            Name = request.Name,
            Description = request.Description,
            InputSchemaJson = request.InputSchemaJson,
            PromptTemplate = request.PromptTemplate,
            AllowedToolNamesJson = request.AllowedToolNamesJson,
            IsChatTriggerable = request.IsChatTriggerable,
            ChatTriggerHintsJson = string.IsNullOrWhiteSpace(request.ChatTriggerHintsJson) ? "[]" : request.ChatTriggerHintsJson,
            CreatedByRole = User.FindFirstValue(ClaimTypes.Role) ?? Roles.Admin
        };

        await _workflows.AddDefinitionAsync(definition, ct);
        return Ok(ToDto(definition));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<WorkflowRunDto>>> ListRuns(CancellationToken ct)
    {
        var runs = await _workflows.ListRunsAsync(ct);
        return Ok(runs.Select(ToDto));
    }

    [HttpPost("{id:int}/run")]
    public async Task<ActionResult<WorkflowRunResponse>> RunStructured(int id, RunWorkflowRequest request, CancellationToken ct)
    {
        var outcome = await _workflows.RunStructuredAsync(id, request.Inputs, DescribeTrigger($"POST /api/workflows/{id}/run"), User.ToCallerIdentity(), ct);
        return ToActionResult(outcome, fellBackToQuery: false, fallbackRun: null);
    }

    [HttpPost("chat")]
    public async Task<ActionResult<WorkflowRunResponse>> RunFromChat(WorkflowChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Text is required.");
        }

        var chatOutcome = await _workflows.RunFromChatAsync(request.Text, DescribeTrigger("POST /api/workflows/chat"), User.ToCallerIdentity(), ct);
        return chatOutcome.FellBackToQuery
            ? Ok(new WorkflowRunResponse(FellBackToQuery: true, ToDto(chatOutcome.FallbackRun!), WorkflowRun: null))
            : ToActionResult(chatOutcome.WorkflowResult!, fellBackToQuery: false, fallbackRun: null);
    }

    private ActionResult<WorkflowRunResponse> ToActionResult(WorkflowRunOutcome outcome, bool fellBackToQuery, AgentRunLog? fallbackRun) =>
        outcome.Status switch
        {
            WorkflowRunStatus.NotFound => NotFound(outcome.Error),
            WorkflowRunStatus.ValidationError => BadRequest(outcome.Error),
            WorkflowRunStatus.Blocked => Conflict(outcome.Error),
            WorkflowRunStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, outcome.Error),
            _ => Ok(new WorkflowRunResponse(fellBackToQuery, ToDto(outcome.Run!), ToDto(outcome.WorkflowRun!)))
        };

    private string DescribeTrigger(string endpoint)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "Unknown";
        var actor = User.Identity?.Name ?? "Unknown";
        return $"{role} ({actor}) via {endpoint}";
    }

    private static WorkflowDefinitionDto ToDto(WorkflowDefinition d) => new(
        d.Id, d.Name, d.Description, d.InputSchemaJson, d.PromptTemplate, d.AllowedToolNamesJson,
        d.IsChatTriggerable, d.ChatTriggerHintsJson, d.IsActive, d.CreatedByRole, d.CreatedAtUtc);

    private static WorkflowRunDto ToDto(WorkflowRun r) => new(
        r.Id, r.WorkflowDefinitionId, r.WorkflowDefinition?.Name ?? "(unknown)", r.AgentRunLogId,
        r.InputValuesJson, r.TriggerSource.ToString(), r.RawChatInput, r.MatchConfidence, r.CreatedAtUtc);

    private static AgentRunLogDto ToDto(AgentRunLog r) => new(
        r.Id, r.ClaimId, r.Trigger, r.Prompt, r.ToolCallsJson, r.FinalAnswer, r.ModelId, r.ToolCallCount,
        r.ReasoningText, r.InputTokenCount, r.OutputTokenCount, r.TotalTokenCount,
        r.InputCost, r.OutputCost, r.TotalCost, r.CreatedAtUtc, r.Outcome);
}
