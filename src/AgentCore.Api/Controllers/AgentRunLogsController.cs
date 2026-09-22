using AgentCore.Api.Contracts;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

/// <summary>Read-only audit trail of every agent invocation (per section 7 of docs/plan.md).</summary>
[ApiController]
[Route("api/agent/runs")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class AgentRunLogsController : ControllerBase
{
    private readonly IAgentRunLogRepository _runLogs;

    public AgentRunLogsController(IAgentRunLogRepository runLogs) => _runLogs = runLogs;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentRunLogDto>>> GetAll(CancellationToken ct)
    {
        var runs = await _runLogs.GetAllAsync(ct);
        return Ok(runs.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgentRunLogDto>> GetById(int id, CancellationToken ct)
    {
        var run = await _runLogs.GetByIdAsync(id, ct);
        return run is null ? NotFound() : Ok(ToDto(run));
    }

    private static AgentRunLogDto ToDto(AgentRunLog r) => new(
        r.Id, r.ClaimId, r.Trigger, r.Prompt, r.ToolCallsJson, r.FinalAnswer, r.ModelId, r.ToolCallCount,
        r.ReasoningText, r.InputTokenCount, r.OutputTokenCount, r.TotalTokenCount,
        r.InputCost, r.OutputCost, r.TotalCost, r.CreatedAtUtc, r.Outcome);
}
