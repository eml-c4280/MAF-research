using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentCore.Agents;

/// <summary>
/// Builds the compaction pipeline applied to every agent conversation, so a long-running claim
/// investigation (several tool calls plus history/policy lookups) or a chatty free-form
/// <c>/api/agent/query</c> session never silently overflows the model's context window. Stages run
/// in order, cheapest/least-lossy first:
/// 1. <see cref="ToolResultCompactionStrategy"/> - drop old tool call/result payloads, usually the
///    single biggest contributor to context growth in this app (claim history/search results).
/// 2. <see cref="SummarizationCompactionStrategy"/> - once trimming tool output isn't enough,
///    replace older turns with a model-generated summary (uses the same chat client/model).
/// 3. <see cref="SlidingWindowCompactionStrategy"/> - keep only the most recent turns once the
///    conversation is simply long, independent of token size.
/// 4. <see cref="TruncationCompactionStrategy"/> - last resort hard cutoff so a single
///    pathological turn can never blow the budget.
/// Returns the stable <see cref="AIContextProvider"/> base type so callers don't need to opt into
/// the experimental Compaction API (MAAI001) themselves.
/// </summary>
public class Compactions
{
    private readonly CompactionOptions _options;

    public Compactions(IOptions<AgentOptions> options)
    {
        _options = options.Value.Compaction;
    }

#pragma warning disable MAAI001 // Microsoft.Agents.AI.Compaction is experimental as of 1.21.0.
    public AIContextProvider CreateProvider(IChatClient client)
    {
        var pipeline = new PipelineCompactionStrategy(
        [
            new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(_options.ToolResultTriggerTokens)),
            new SummarizationCompactionStrategy(client, CompactionTriggers.TokensExceed(_options.SummarizationTriggerTokens)),
            new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(_options.MaxTurns)),
            new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(_options.MaxContextTokens))
        ]);

        return new CompactionProvider(pipeline);
    }
#pragma warning restore MAAI001
}
