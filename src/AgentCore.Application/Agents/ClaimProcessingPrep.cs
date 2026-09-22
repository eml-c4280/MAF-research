using AgentCore.Domain.Entities;

namespace AgentCore.Application.Agents;

/// <summary>
/// Result of ClaimAgentService.PrepareClaimForProcessingAsync (Phase 13, docs/plan-agents.md §7):
/// either a blocking status (NotFound/Forbidden/Blocked, Claim/InitialContext null) decided
/// *before* any agent runs, or Completed with the claim reference and the deterministic
/// coverage/escalation/risk facts (docs/business-logic.md §5) already resolved into the initial
/// pipeline context, ready to hand to the first workflow step.
/// </summary>
public record ClaimProcessingPrep(
    ClaimProcessingStatus Status, string? BlockReason, Claim? Claim, Dictionary<string, string>? InitialContext);
