using AgentCore.Infrastructure;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// docs/plan.md section 5 (OBO): every tool that touches a specific worker reads the caller's
// asserted identity (X-Caller-User-Id/X-Caller-Role, set only by the trusted AgentCore.Api
// process) via this scoped context, and enforces the same permission boundary the REST API does.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CallerContext>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "ClaimsToolsServer", Version = "1.0.0" };
    })
    .WithToolsFromAssembly()
    .WithHttpTransport();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();

// Exposed so WebApplicationFactory<Program> (tests/AgentCore.Agents.Tests) can host this app
// in-process - top-level statements otherwise generate an internal Program class.
public partial class Program;
