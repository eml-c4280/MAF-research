using AgentCore.Domain.Entities;
using AgentCore.Domain.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace AgentCore.Api.Hubs;

public class SignalRAgentActivityPublisher : IAgentActivityPublisher
{
    private readonly IHubContext<AgentActivityHub> _hub;

    public SignalRAgentActivityPublisher(IHubContext<AgentActivityHub> hub) => _hub = hub;

    public Task RunStartedAsync(int runId, int? claimId, string prompt, CancellationToken ct = default) =>
        Send(runId, claimId, "RunStarted", new { runId, claimId, prompt }, ct);

    public Task ToolCallStartedAsync(int runId, int? claimId, string toolName, string argumentsJson, CancellationToken ct = default) =>
        Send(runId, claimId, "ToolCallStarted", new { runId, toolName, argumentsJson }, ct);

    public Task ToolCallCompletedAsync(int runId, int? claimId, string toolName, string? result, CancellationToken ct = default) =>
        Send(runId, claimId, "ToolCallCompleted", new { runId, toolName, result }, ct);

    public Task RunCompletedAsync(AgentRunLog run, CancellationToken ct = default) =>
        Send(run.Id, run.ClaimId, "RunCompleted", new
        {
            runId = run.Id,
            finalAnswer = run.FinalAnswer,
            modelId = run.ModelId,
            toolCallCount = run.ToolCallCount,
            reasoningText = run.ReasoningText,
            inputTokenCount = run.InputTokenCount,
            outputTokenCount = run.OutputTokenCount,
            totalTokenCount = run.TotalTokenCount,
            inputCost = run.InputCost,
            outputCost = run.OutputCost,
            totalCost = run.TotalCost,
            outcome = run.Outcome
        }, ct);

    public Task RunFailedAsync(int runId, int? claimId, string errorMessage, CancellationToken ct = default) =>
        Send(runId, claimId, "RunFailed", new { runId, errorMessage }, ct);

    private Task Send(int runId, int? claimId, string method, object payload, CancellationToken ct)
    {
        var groups = new List<string> { AgentActivityHub.GroupForRun(runId) };
        if (claimId.HasValue)
        {
            groups.Add(AgentActivityHub.GroupForClaim(claimId.Value));
        }

        return _hub.Clients.Groups(groups).SendAsync(method, payload, ct);
    }
}
