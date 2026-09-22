namespace AgentCore.Api.Contracts;

public record ClaimDto(
    int Id,
    int WorkerId,
    string ClaimNumber,
    DateOnly ClaimDate,
    string ClaimType,
    decimal Amount,
    string Status,
    string Description,
    string? ReviewedBy,
    DateTime? ReviewedAtUtc,
    string? AgentRecommendation,
    double? AgentConfidence);

public record UpsertClaimRequest(
    int WorkerId,
    string ClaimNumber,
    DateOnly ClaimDate,
    string ClaimType,
    decimal Amount,
    string Status,
    string Description);

public record ClaimsHistoryResponse(
    int WorkerId,
    string WorkerCode,
    string WorkerName,
    int Years,
    int TotalClaims,
    decimal TotalAmount,
    decimal AverageAmount,
    IReadOnlyList<ClaimDto> Claims,
    IReadOnlyDictionary<int, int> ByYear,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> ByType);
