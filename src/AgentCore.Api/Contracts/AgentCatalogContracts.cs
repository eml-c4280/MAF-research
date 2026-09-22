namespace AgentCore.Api.Contracts;

/// <summary>Phase 13 (docs/plan-agents.md §8) - the agent catalog exposed for discovery.</summary>
public record AgentCatalogEntryDto(string Name, string DisplayName, IReadOnlyList<string> ToolNames);
