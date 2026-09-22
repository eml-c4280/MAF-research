using AgentCore.Domain.Notifications;
using AgentCore.Domain.Repositories;
using AgentCore.Infrastructure.Notifications;
using AgentCore.Infrastructure.Persistence;
using AgentCore.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AgentCoreDb")
            ?? throw new InvalidOperationException("Missing required connection string 'AgentCoreDb'.");

        services.AddDbContext<AgentCoreDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IWorkerRepository, WorkerRepository>();
        services.AddScoped<IInsurancePolicyRepository, InsurancePolicyRepository>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IPendingActionRepository, PendingActionRepository>();
        services.AddScoped<IAgentRunLogRepository, AgentRunLogRepository>();
        services.AddScoped<IConversationSessionRepository, ConversationSessionRepository>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowRunRepository, WorkflowRunRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
