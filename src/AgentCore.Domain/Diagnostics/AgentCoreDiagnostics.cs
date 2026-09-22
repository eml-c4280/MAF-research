using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentCore.Domain.Diagnostics;

/// <summary>
/// Shared ActivitySource/Meter for the custom agent-run signal that auto-instrumentation can't
/// see (ASP.NET Core/EF Core/HttpClient instrumentation covers the request/DB/outbound-call
/// shape, but not "how long did this agent run take" or "how many payouts got queued").
/// Lives in Domain so both AgentCore.Agents (sensitive tools) and AgentCore.Application
/// (ClaimAgentService) can record against it without a circular project reference.
/// </summary>
public static class AgentCoreDiagnostics
{
    public const string SourceName = "AgentCore";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> AgentRuns =
        Meter.CreateCounter<long>("agentcore.agent.runs", description: "Number of agent runs, tagged by mode and outcome.");

    public static readonly Histogram<double> AgentRunDuration =
        Meter.CreateHistogram<double>("agentcore.agent.run.duration", unit: "s", description: "Duration of agent runs in seconds.");

    public static readonly Counter<long> PendingActionsQueued =
        Meter.CreateCounter<long>("agentcore.pending_actions.queued", description: "Number of PendingActions queued for approval, tagged by action type.");
}
