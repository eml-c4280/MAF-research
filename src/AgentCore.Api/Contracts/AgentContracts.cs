namespace AgentCore.Api.Contracts;

public record AgentQueryRequest(string Prompt);

public record AgentRunLogDto(
    int Id,
    int? ClaimId,
    string Trigger,
    string Prompt,
    string ToolCallsJson,
    string FinalAnswer,
    string ModelId,
    int ToolCallCount,
    string? ReasoningText,
    int? InputTokenCount,
    int? OutputTokenCount,
    int? TotalTokenCount,
    decimal? InputCost,
    decimal? OutputCost,
    decimal? TotalCost,
    DateTime CreatedAtUtc,
    string Outcome);

public record PendingActionDto(
    int Id,
    int ClaimId,
    string ActionType,
    string Payload,
    string Status,
    DateTime RequestedAtUtc,
    string? DecidedByRole,
    DateTime? DecidedAtUtc,
    DateTime ExpiresAt,
    bool IsExpired,
    string? ExecutionError,
    string? RuleOutputsJson);

public record ProcessClaimResponse(
    AgentRunLogDto Run,
    int ClaimId,
    string Recommendation,
    string ClaimStatus,
    IReadOnlyList<PendingActionDto> QueuedActions);
