using System.Text;
using AgentCore.Agents;
using AgentCore.Api.Hubs;
using AgentCore.Application;
using AgentCore.Application.Auth;
using AgentCore.Domain.Diagnostics;
using AgentCore.Domain.Realtime;
using AgentCore.Infrastructure;
using AgentCore.Infrastructure.Persistence;
using AgentCore.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Real JWT auth (docs/plan.md section 5) - replaces the old X-Role header trust model entirely.
// A CaseManager's token carries only ["CaseManager"]; Admin carries ["Admin","CaseManager"];
// SuperAdmin carries all three, so every existing [Authorize(Roles=...)] check that accepts
// Admin/CaseManager already accepts SuperAdmin too, with no SuperAdmin-specific logic anywhere.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Any endpoint without an explicit [Authorize]/[AllowAnonymous] still requires a valid JWT.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Required for the browser-based UI (docs/plan-ui.md) to call this API from a different origin
// - a plain <script>/curl/Swagger request doesn't need this, but a fetch/axios call from a page
// served on a different port is blocked by the browser without it. Comma-separated so both the
// Vite dev server and a future dockerized UI origin can be allowed without a code change.
// Defaults cover 5173 AND 5174: Vite auto-increments to the next free port whenever something
// (another `npm run dev`, a leftover background instance, etc.) already holds 5173, so a
// single allowed origin here is a recurring source of "it worked yesterday" CORS failures.
var uiOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173,http://localhost:5174")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("Ui", policy => policy
    .WithOrigins(uiOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token from POST /api/auth/login. Click Authorize to apply it to all calls."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgents(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddScoped<IAgentActivityPublisher, SignalRAgentActivityPublisher>();

// Observability: traces + metrics auto-instrumented for ASP.NET Core/HttpClient/EF Core, plus
// the custom AgentCoreDiagnostics source/meter for agent-run signal (see docs/plan.md section 8).
// Traces + logs ship via OTLP to the collector (Docker Compose, Phase 7); metrics are exposed
// directly on /metrics for Prometheus to scrape - both work with no collector present (traces/
// logs simply fail to export until one exists; this local dev default points at the standard
// OTLP port so it "just works" once Phase 7's compose stack is up).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("AgentCore.Api"))
    .WithTracing(tracing => tracing
        .AddSource(AgentCoreDiagnostics.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(AgentCoreDiagnostics.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
});

var app = builder.Build();

// Apply pending EF Core migrations and load sample data on startup, so the API is ready to
// exercise via Swagger without a manual migration/seed step (MSSQL runs in Docker Compose).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentCoreDbContext>();
    var superAdminEmail = builder.Configuration["Seed:SuperAdminEmail"] ?? "superadmin@agentcore.local";
    var superAdminPassword = builder.Configuration["Seed:SuperAdminPassword"] ?? "SuperAdmin123!";
    await AgentCoreDbSeeder.SeedAsync(db, superAdminEmail, superAdminPassword);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Ui");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AgentActivityHub>("/hubs/agent-activity");

// Anonymous: Prometheus scrapes this directly and won't send our JWT bearer token;
// this is metrics-only (no business data), so it's fine to leave off the auth requirement.
app.MapPrometheusScrapingEndpoint().AllowAnonymous();

app.Run();

// Exposes the top-level Program for WebApplicationFactory-based integration testing.
public partial class Program;
