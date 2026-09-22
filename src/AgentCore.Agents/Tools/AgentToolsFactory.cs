using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AgentCore.Agents.Tools;

/// <summary>
/// Discovers the claim/worker tool set from the ClaimsToolsServer MCP server and assembles the
/// per-run <see cref="AITool"/> list, so <see cref="WorkerClaimAgentFactory"/> doesn't need to
/// know which tools exist or talk to MCP itself.
///
/// Connects fresh per call, one deliberate reversal of an earlier decision (docs/plan.md section
/// 5): this used to be a single connection created once and reused forever, specifically because
/// "nothing about a tool call needs to be correlated to a specific agent run at connection time"
/// (docs/plan-mcp.md section 4). That's no longer true - row-level permission enforcement in
/// mcp/ClaimsToolsServer now depends on *which user* is running, so every connection asserts that
/// run's <see cref="CallerIdentity"/> via headers the untrusted end caller never sees or sets
/// (only this server-side code does, after already validating the caller's own JWT). The extra
/// per-run MCP handshake is a real latency cost, accepted as the correct tradeoff for real
/// authorization over the old performance-first shortcut.
/// </summary>
public class AgentToolsFactory
{
    public const string CallerUserIdHeaderName = "X-Caller-User-Id";
    public const string CallerRoleHeaderName = "X-Caller-Role";

    // MCP has no native "sensitive" concept - the server exposes one flat tool list. The
    // auto/sensitive split that gates which tools a run may use stays a client-side concern,
    // keyed by the same tool names the server registers them under.
    private static readonly HashSet<string> SensitiveToolNames =
        ["WorkerEmailSender", "EscalationEmailSender", "PayoutCalculator"];

    private readonly string? _endpoint;
    private readonly McpClient? _fixedClientForTests;

    public AgentToolsFactory(IOptions<AgentOptions> options)
    {
        _endpoint = options.Value.ClaimsToolsServerUrl;
    }

    /// <summary>Accepts an already-connected client directly - used by tests to point this
    /// factory at an in-process WebApplicationFactory-hosted server instead of a real endpoint.
    /// Bypasses caller-identity header propagation entirely (tests don't exercise permission
    /// enforcement, only tool discovery).</summary>
    public AgentToolsFactory(McpClient client)
    {
        _fixedClientForTests = client;
    }

    public async Task<List<AITool>> BuildToolsetAsync(
        bool includeSensitiveTools, CallerIdentity caller, CancellationToken ct = default, IReadOnlySet<string>? excludeToolNames = null)
    {
        var client = await ConnectAsync(caller, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        return tools
            .Where(t => includeSensitiveTools || !SensitiveToolNames.Contains(t.Name))
            .Where(t => excludeToolNames is null || !excludeToolNames.Contains(t.Name))
            .Cast<AITool>()
            .ToList();
    }

    /// <summary>Workflow variant (docs/plan.md §11): an explicit allowlist rather than the
    /// binary auto/sensitive split above - a WorkflowDefinition.AllowedToolNamesJson names
    /// exactly the tools its agent may see, sensitive or not.</summary>
    public async Task<List<AITool>> BuildToolsetAsync(IReadOnlySet<string> allowedToolNames, CallerIdentity caller, CancellationToken ct = default)
    {
        var client = await ConnectAsync(caller, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);

        return tools
            .Where(t => allowedToolNames.Contains(t.Name))
            .Cast<AITool>()
            .ToList();
    }

    private async Task<McpClient> ConnectAsync(CallerIdentity caller, CancellationToken ct)
    {
        if (_fixedClientForTests is not null)
        {
            return _fixedClientForTests;
        }

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add(CallerUserIdHeaderName, caller.UserId.ToString());
        httpClient.DefaultRequestHeaders.Add(CallerRoleHeaderName, string.Join(",", caller.Roles));

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(_endpoint!), TransportMode = HttpTransportMode.StreamableHttp },
            httpClient);

        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
