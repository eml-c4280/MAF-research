using AgentCore.Agents;
using AgentCore.Agents.Tools;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace AgentCore.Agents.Tests;

/// <summary>
/// Hosts the real ClaimsToolsServer MCP server in-process (WebApplicationFactory, real
/// Streamable HTTP transport riding an in-memory HttpMessageHandler instead of a socket) and
/// exercises AgentToolsFactory against it over a genuine MCP ListToolsAsync() round trip - a
/// copy-pasted `[McpServerTool(Name = "...")]` string, or a duplicate/missing
/// [McpServerToolType] registration, fails a test run here instead of silently breaking tool
/// routing at runtime, where the model would only ever see one of two identically-named tools.
/// No database is touched: tools/list is pure reflection over registered tool methods, never
/// invokes them, so a syntactically-present-but-unreachable connection string is enough.
/// </summary>
public class AgentToolsFactoryTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    // Tool listing is identical for every caller (docs/plan-mcp.md's tools/list is pure reflection,
    // never permission-checked) - an arbitrary Admin identity is enough for these tests, which only
    // assert on which tools come back, not on any permission enforcement.
    private static readonly CallerIdentity TestCaller = new(1, ["Admin"]);

    private readonly WebApplicationFactory<Program> _factory;

    public AgentToolsFactoryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AgentCoreDb"] = "Server=(local);Database=Unused;Trusted_Connection=True;"
                })));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<AgentToolsFactory> CreateSubjectAsync()
    {
        var httpClient = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient);

        var mcpClient = await McpClient.CreateAsync(transport);
        return new AgentToolsFactory(mcpClient);
    }

    [Fact]
    public async Task BuildToolsetAsync_IncludingSensitiveTools_HasNoDuplicateNames()
    {
        var factory = await CreateSubjectAsync();
        var tools = await factory.BuildToolsetAsync(includeSensitiveTools: true, TestCaller);

        var duplicateNames = tools
            .GroupBy(t => t.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicateNames);
    }

    [Fact]
    public async Task BuildToolsetAsync_ExcludingSensitiveTools_OnlyReturnsAutoAndRuleTools()
    {
        var factory = await CreateSubjectAsync();
        var tools = await factory.BuildToolsetAsync(includeSensitiveTools: false, TestCaller);

        // 3 auto tools + 3 rule tools (CV/ES/FR - docs/business-logic.md §4) - rule tools have no
        // side effect, so they're freely callable the same as auto tools.
        Assert.Equal(6, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name is "WorkerEmailSender" or "EscalationEmailSender" or "PayoutCalculator");
    }

    [Fact]
    public async Task BuildToolsetAsync_IncludingSensitiveTools_ReturnsAllNineExpectedNames()
    {
        var factory = await CreateSubjectAsync();
        var tools = await factory.BuildToolsetAsync(includeSensitiveTools: true, TestCaller);

        var expectedNames = new[]
        {
            "WorkerInformationFetcher", "ClaimsSearcher", "WorkerClaimsHistoryFetcher",
            "CoverageChecker", "EscalationEvaluator", "ClaimRiskScorer",
            "WorkerEmailSender", "EscalationEmailSender", "PayoutCalculator"
        };

        Assert.Equal(expectedNames.ToHashSet(), tools.Select(t => t.Name).ToHashSet());
    }
}
