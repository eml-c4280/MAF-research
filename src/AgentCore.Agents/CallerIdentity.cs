namespace AgentCore.Agents;

/// <summary>
/// The authenticated user on whose behalf an agent run/tool call is happening (docs/plan.md
/// section 5, docs/knowledge-base.md Topic 4 - OBO). Carried into <see cref="AgentToolsFactory"/>
/// so the MCP connection for that run asserts this identity to mcp/ClaimsToolsServer, which
/// enforces the same permission boundary the REST API does - never a blanket trusted-service
/// identity the tools implicitly inherit.
/// </summary>
public record CallerIdentity(int UserId, IReadOnlyCollection<string> Roles);
