using AgentCore.Agents.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));

        // Singleton: the claim/worker tools now live in mcp/ClaimsToolsServer, discovered over
        // MCP rather than instantiated in-process - AgentToolsFactory just holds a reusable MCP
        // connection, with no per-run scoped state left to worry about (see docs/plan-mcp.md).
        services.AddSingleton<AgentToolsFactory>();

        services.AddSingleton<Compactions>();
        services.AddScoped<WorkerClaimAgentFactory>();

        return services;
    }
}
