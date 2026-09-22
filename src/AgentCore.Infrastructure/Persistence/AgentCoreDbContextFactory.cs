using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentCore.Infrastructure.Persistence;

/// <summary>
/// Used only by the `dotnet ef` CLI (migrations add/update) at design time, so migrations can
/// be authored without the API project's DI/composition root being wired up yet. Not used at
/// application runtime — the real connection string there comes from AgentCore.Api configuration.
/// </summary>
public class AgentCoreDbContextFactory : IDesignTimeDbContextFactory<AgentCoreDbContext>
{
    public AgentCoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AGENTCORE_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=AgentCoreDb;User Id=sa;Password=Your_password123;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<AgentCoreDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AgentCoreDbContext(optionsBuilder.Options);
    }
}
