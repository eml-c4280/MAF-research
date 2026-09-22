using AgentCore.Domain.Entities;

namespace AgentCore.Application.Agents;

public record ConversationTurnResult(AgentRunLog Run, ConversationSession Session);

public record ConversationSessionMessagesResult(ConversationSession Session, IReadOnlyList<AgentRunLog> Turns);
