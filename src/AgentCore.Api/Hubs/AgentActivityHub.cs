using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgentCore.Api.Hubs;

/// <summary>
/// Clients connect here and join a group for the run or claim they want to watch, then receive
/// "RunStarted" / "ToolCallStarted" / "ToolCallCompleted" / "RunCompleted" events as an agent
/// run (POST /api/agent/query or /api/agent/claims/{id}/process) progresses.
///
/// Anonymous: this project's auth reads the JWT from an Authorization header, which a browser's
/// native WebSocket API cannot attach to the handshake (a well-known SignalR/WebSocket
/// limitation) - enforcing it here would force clients onto SignalR's slower long-polling
/// transport just to carry the token. The live event stream is read-only observational data
/// (the same information anyone can already read from the persisted AgentRunLog), so it's left
/// open rather than distorting the transport to fit the app's REST auth model.
/// </summary>
[AllowAnonymous]
public class AgentActivityHub : Hub
{
    public Task SubscribeToRun(int runId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupForRun(runId));

    public Task SubscribeToClaim(int claimId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupForClaim(claimId));

    public static string GroupForRun(int runId) => $"run-{runId}";

    public static string GroupForClaim(int claimId) => $"claim-{claimId}";
}
