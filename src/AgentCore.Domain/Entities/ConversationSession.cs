using AgentCore.Domain.Enums;

namespace AgentCore.Domain.Entities;

/// <summary>
/// A persisted multi-turn chat session (docs/plan.md section 14). Backs free-form Q&amp;A only -
/// claim processing stays single-shot by design. Each turn in the conversation is still exactly
/// one <see cref="AgentRunLog"/> row (via <see cref="AgentRunLog.ConversationSessionId"/>); this
/// entity's own job is just to carry the framework's own session state between HTTP requests.
/// </summary>
public class ConversationSession
{
    public int Id { get; set; }
    public ConversationSessionStatus Status { get; set; } = ConversationSessionStatus.Active;
    public string CreatedByRole { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;

    /// <summary>Set from the first message once the session has one - null for a brand-new,
    /// empty session, e.g. right after "New chat" before anything has been typed.</summary>
    public string? Title { get; set; }

    /// <summary>Opaque state blob from <c>AIAgent.SerializeSessionAsync</c> - the framework's own
    /// tracked message history, replayed via <c>AIAgent.DeserializeSessionAsync</c> on the next
    /// turn. This app never parses it; a chat thread for display is rendered from the linked
    /// <see cref="AgentRunLog"/> rows instead, not from this blob.</summary>
    public string SerializedStateJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;
}
