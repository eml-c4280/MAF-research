using AgentCore.Domain.Entities;

namespace AgentCore.Application.Agents;

public record ClaimProcessingResult(AgentRunLog Run, Claim Claim, IReadOnlyList<PendingAction> QueuedActions);
