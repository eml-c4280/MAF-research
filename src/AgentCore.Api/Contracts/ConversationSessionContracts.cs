namespace AgentCore.Api.Contracts;

public record ConversationSessionDto(
    int Id,
    string Status,
    string? Title,
    string CreatedByRole,
    string CreatedByName,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc);

public record SendSessionMessageRequest(string Message);

public record ConversationSessionMessagesResponse(
    ConversationSessionDto Session,
    IReadOnlyList<AgentRunLogDto> Turns);
