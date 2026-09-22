namespace ClaimsToolsServer.Tools;

/// <summary>
/// Returned by every sensitive tool instead of a hand-written sentence, so the host
/// (AgentCore.Application's ClaimAgentService, running in a different process) can stamp
/// PendingAction.ProposedByAgentRunId itself right after observing this tool call's result -
/// see docs/plan-mcp.md section 4 for why that FK can't be set here, in the MCP server.
/// DenialReason is set (with PendingActionId left at 0, no row ever queued) when the caller lacks
/// permission for the claim's worker (docs/plan.md section 5) - the backfill code in
/// ClaimAgentService just logs a harmless "not found" for id 0 and leaves ProposedByAgentRunId
/// unset, same as it already does for any other unresolvable id.
/// </summary>
public record PendingActionRef(int PendingActionId, string ActionType, string? DenialReason = null);
