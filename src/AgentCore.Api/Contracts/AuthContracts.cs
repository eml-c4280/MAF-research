namespace AgentCore.Api.Contracts;

public record LoginRequest(string Email, string Password);

public record UserDto(
    int Id,
    string Email,
    string Name,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

public record CreateUserRequest(string Name, string Email, string Password, string Role);

public record UpdateUserRequest(string Name, string Email);

public record ResetPasswordRequest(string NewPassword);
