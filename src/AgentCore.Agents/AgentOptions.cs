namespace AgentCore.Agents;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public string OllamaHost { get; set; } = "http://localhost:11434";
    public string ModelId { get; set; } = "qwen3:0.6b";

    /// <summary>Base URL of the ClaimsToolsServer MCP endpoint (mcp/ClaimsToolsServer). All claim/
    /// worker tools are discovered from here rather than instantiated in-process - see
    /// docs/plan-mcp.md.</summary>
    public string ClaimsToolsServerUrl { get; set; } = "http://localhost:5050/mcp";

    /// <summary>Price per 1,000,000 input/output tokens, used to compute AgentRunLog's cost fields.
    /// Defaults to 0 - a local Ollama model has no per-token cost. Set these if you point
    /// Agent:OllamaHost at a paid, metered provider instead.</summary>
    public decimal InputPricePerMillionTokens { get; set; } = 0m;
    public decimal OutputPricePerMillionTokens { get; set; } = 0m;

    /// <summary>Loop guard (docs/plan.md section 13): caps how many tool-call/response rounds a
    /// single agent.RunAsync call may go through before Microsoft.Extensions.AI's
    /// FunctionInvokingChatClient stops the loop on its own and returns whatever it has. Wired in
    /// via WorkerClaimAgentFactory, not the framework's default (40), since a small local model
    /// looping on tool selection should stop well short of that.</summary>
    public int MaxToolCallsPerRun { get; set; } = 8;

    /// <summary>Loop guard (docs/plan.md section 13): wall-clock cap on a single agent run,
    /// enforced in ClaimAgentService.ExecuteRunAsync via a linked CancellationTokenSource. A trip
    /// is recorded as AgentRunLog.Outcome = "Timeout" and returned as a non-crashing result rather
    /// than left to hang the caller or a SignalR watcher indefinitely.</summary>
    public TimeSpan MaxRunDuration { get; set; } = TimeSpan.FromSeconds(90);

    public CompactionOptions Compaction { get; set; } = new();
}

/// <summary>
/// Thresholds for the compaction pipeline built by <see cref="Compactions"/> (see that class for
/// the stage order/rationale). Defaults are sized for qwen3:0.6b's small context window - Ollama
/// doesn't have an explicit `num_ctx` override configured for it anywhere in this repo, so these
/// stay conservative; retune them (via the "Agent:Compaction" config section) if the deployed
/// model/host is configured with a larger context window.
/// </summary>
public class CompactionOptions
{
    /// <summary>Once accumulated tool call/result content exceeds this many tokens, older tool
    /// payloads are dropped first - the cheapest thing to lose, and usually the largest single
    /// contributor to context growth (claim histories, search results, etc.).</summary>
    public int ToolResultTriggerTokens { get; set; } = 512;

    /// <summary>Once the conversation exceeds this many tokens (after tool trimming), older turns
    /// are replaced with a model-generated summary rather than dropped outright.</summary>
    public int SummarizationTriggerTokens { get; set; } = 1280;

    /// <summary>Regardless of size, keep only the most recent N turns once the conversation runs
    /// this long - bounds worst-case latency/cost even for a short but very chatty exchange.</summary>
    public int MaxTurns { get; set; } = 4;

    /// <summary>Hard ceiling: truncate outright if a run still exceeds this many tokens after
    /// every earlier stage, so a single pathological turn can never blow the model's context
    /// window.</summary>
    public int MaxContextTokens { get; set; } = 3072;
}
