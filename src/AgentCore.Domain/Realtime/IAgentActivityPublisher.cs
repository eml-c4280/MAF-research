using AgentCore.Domain.Entities;

namespace AgentCore.Domain.Realtime;

/// <summary>
/// Publishes live progress of an agent run for a UI to watch as it happens (e.g. over SignalR),
/// in addition to - not instead of - the AgentRunLog persisted once the run finishes. A no-op
/// implementation is valid (e.g. in tests) since this is purely for observers, not correctness.
/// </summary>
public interface IAgentActivityPublisher
{
    Task RunStartedAsync(int runId, int? claimId, string prompt, CancellationToken ct = default);

    Task ToolCallStartedAsync(int runId, int? claimId, string toolName, string argumentsJson, CancellationToken ct = default);

    Task ToolCallCompletedAsync(int runId, int? claimId, string toolName, string? result, CancellationToken ct = default);

    /// <summary>Takes the fully-populated run (model, token usage, cost, tool call count, reasoning
    /// text, final answer) so a watching client sees everything the persisted AgentRunLog has.</summary>
    Task RunCompletedAsync(AgentRunLog run, CancellationToken ct = default);

    /// <summary>Published instead of RunCompleted when the run throws, so a watching client always gets a terminal event.</summary>
    Task RunFailedAsync(int runId, int? claimId, string errorMessage, CancellationToken ct = default);
}
