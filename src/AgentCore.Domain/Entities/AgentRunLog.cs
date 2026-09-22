namespace AgentCore.Domain.Entities;

public class AgentRunLog
{
    public int Id { get; set; }
    public int? ClaimId { get; set; }
    public Claim? Claim { get; set; }

    /// <summary>What caused this run, e.g. "Manager via POST /agent/claims/12/process".</summary>
    public string Trigger { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;

    /// <summary>JSON array of the tool calls (and results) the agent made during this run.</summary>
    public string ToolCallsJson { get; set; } = string.Empty;
    public string FinalAnswer { get; set; } = string.Empty;

    /// <summary>The model that served this run (from AgentOptions.ModelId at the time), e.g. "qwen3:0.6b".</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>How many tool calls the agent made - counted directly from ToolCallsJson, not the model's own report.</summary>
    public int ToolCallCount { get; set; }

    /// <summary>The model's own "thinking"/chain-of-thought output, when it exposes one (e.g. qwen3's reasoning
    /// mode via Microsoft.Extensions.AI's TextReasoningContent) - separate from FinalAnswer, which is only the
    /// user-facing text. Null when the model doesn't emit reasoning content.</summary>
    public string? ReasoningText { get; set; }

    public int? InputTokenCount { get; set; }
    public int? OutputTokenCount { get; set; }
    public int? TotalTokenCount { get; set; }

    /// <summary>Derived from token counts x AgentOptions' configured per-token price. Always 0 for the default
    /// local Ollama setup (nothing to charge for) - present so a paid provider can be dropped in without a
    /// schema change.</summary>
    public decimal? InputCost { get; set; }
    public decimal? OutputCost { get; set; }
    public decimal? TotalCost { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>"Success", "GuardTripped" (ToolCallCount hit AgentOptions.MaxToolCallsPerRun),
    /// "Timeout" (exceeded AgentOptions.MaxRunDuration), or "Failed" (an unhandled exception) -
    /// see docs/plan.md section 13 ("Loop guards"). Distinct from the same-named metric tag
    /// recorded on agentcore.agent.runs, which existed first but was never persisted per-run.</summary>
    public string Outcome { get; set; } = "Success";

    /// <summary>Set when this run is one turn of a multi-turn chat (docs/plan.md section 14) -
    /// null for a one-shot query or claim-processing run, same as ClaimId. A chat thread renders
    /// by querying AgentRunLogs filtered by this id, ordered by CreatedAtUtc.</summary>
    public int? ConversationSessionId { get; set; }
    public ConversationSession? ConversationSession { get; set; }
}
