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
        // MCP rather than instantiated in-process (see docs/plan-mcp.md). AgentToolsFactory
        // itself holds no per-connection state - as of Phase 12 (docs/plan.md section 5) it
        // builds a brand-new McpClient connection on every call, carrying that call's caller
        // identity for OBO propagation, so a shared singleton instance is safe even though the
        // connection underneath it is no longer shared across callers.
        services.AddSingleton<AgentToolsFactory>();

        services.AddSingleton<Compactions>();
        services.AddScoped<WorkerClaimAgentFactory>();

        return services;
    }
}
