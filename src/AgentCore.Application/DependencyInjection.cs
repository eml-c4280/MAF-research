using AgentCore.Application.Agents;
using AgentCore.Application.Approvals;
using AgentCore.Application.Auth;
using AgentCore.Application.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddScoped<ClaimAgentService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<WorkflowExecutionService>();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserManagementService>();
        return services;
    }
}
