namespace AgentCore.Api.Contracts;

public record InsurancePolicyDto(
    int Id,
    int WorkerId,
    string PolicyNumber,
    string Provider,
    string CoverageType,
    decimal CoverageAmount,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);

public record UpsertInsurancePolicyRequest(
    int WorkerId,
    string PolicyNumber,
    string Provider,
    string CoverageType,
    decimal CoverageAmount,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);
