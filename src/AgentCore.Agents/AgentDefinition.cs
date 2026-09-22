namespace AgentCore.Agents;

/// <summary>
/// A specialist agent's fixed shape (Phase 13, docs/plan-agents.md §6) - system prompt + tool
/// allowlist, authored in code via AgentCatalog, not a database row. Only *workflows* (which
/// agents to chain, in what order) are Admin-authorable data; the agents themselves are not.
/// </summary>
public record AgentDefinition(
    string Name,
    string DisplayName,
    string Instructions,
    IReadOnlySet<string> ToolNames,
    /// <summary>Falls back to AgentOptions.MaxToolCallsPerRun when null - most specialists don't
    /// need a different guard, but e.g. NotificationAgent should rarely need more than a couple
    /// of tool calls.</summary>
    int? MaxToolCallsPerRunOverride = null);
