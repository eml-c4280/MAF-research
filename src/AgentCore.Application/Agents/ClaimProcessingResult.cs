using AgentCore.Domain.Entities;

namespace AgentCore.Application.Agents;

/// <summary>Phase 13 (docs/plan-agents.md): Run is the *final* pipeline step's AgentRunLog (kept
/// so existing code reading "the answer" off this needs no change); Steps carries the full
/// per-specialist detail - which agent ran, and its own AgentRunLog - for a multi-step
/// "Process Claim" execution. QueuedActions is the union of every PendingAction queued across all
/// steps, not just one.</summary>
public record ClaimProcessingResult(
    AgentRunLog Run, Claim Claim, IReadOnlyList<PendingAction> QueuedActions, IReadOnlyList<ClaimProcessingStepResult> Steps);

public record ClaimProcessingStepResult(string AgentName, AgentRunLog Run);
