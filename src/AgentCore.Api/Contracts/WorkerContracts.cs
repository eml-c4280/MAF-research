namespace AgentCore.Api.Contracts;

public record WorkerDto(
    int Id,
    string Code,
    string Name,
    string Role,
    string Location,
    string Email,
    string PhoneNumber,
    decimal HourlyRate,
    int YearsOfExperience,
    bool IsAvailable,
    int? AssignedCaseManagerUserId);

public record UpsertWorkerRequest(
    string Code,
    string Name,
    string Role,
    string Location,
    string Email,
    string PhoneNumber,
    decimal HourlyRate,
    int YearsOfExperience,
    bool IsAvailable);

public record AssignCaseManagerRequest(int? CaseManagerUserId);
