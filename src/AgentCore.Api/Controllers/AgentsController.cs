using System.Security.Claims;
using AgentCore.Agents;
using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Application.Agents;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

/// <summary>The agent catalog (Phase 13, docs/plan-agents.md §8) - discovery and generic
/// free-form Q&A against any named specialist. Distinct from AgentController, which owns
/// claim-processing/session actions that are more than a plain query.</summary>
[ApiController]
[Route("api/agents")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class AgentsController : ControllerBase
{
    private readonly ClaimAgentService _agentService;

    public AgentsController(ClaimAgentService agentService) => _agentService = agentService;

    [HttpGet]
    public ActionResult<IReadOnlyList<AgentCatalogEntryDto>> List() =>
        Ok(AgentCatalog.All.Select(a => new AgentCatalogEntryDto(a.Name, a.DisplayName, a.ToolNames.ToList())));

    /// <summary>Always read-only regardless of which agent is asked (ClaimAgentService.QueryAsync
    /// passes includeSensitiveTools: false) - an ad-hoc question must never let any agent act,
    /// the same guarantee POST /api/agent/query has always made.</summary>
    [HttpPost("{agentName}/query")]
    public async Task<ActionResult<AgentRunLogDto>> Query(string agentName, AgentQueryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt is required.");
        }

        AgentRunLog run;
        try
        {
            run = await _agentService.QueryAsync(agentName, request.Prompt, DescribeTrigger($"POST /api/agents/{agentName}/query"), User.ToCallerIdentity(), ct);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"No agent named '{agentName}'. See GET /api/agents for the catalog.");
        }

        return Ok(ToDto(run));
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
}
