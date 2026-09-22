using AgentCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Persistence;

public class AgentCoreDbContext : DbContext
{
    public AgentCoreDbContext(DbContextOptions<AgentCoreDbContext> options) : base(options)
    {
    }

    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<PendingAction> PendingActions => Set<PendingAction>();
    public DbSet<AgentRunLog> AgentRunLogs => Set<AgentRunLog>();
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowStepDefinition> WorkflowStepDefinitions => Set<WorkflowStepDefinition>();
    public DbSet<WorkflowRunStep> WorkflowRunSteps => Set<WorkflowRunStep>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Worker>(builder =>
        {
            builder.HasIndex(w => w.Code).IsUnique();
            builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
            builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
            builder.Property(w => w.Role).HasMaxLength(100).IsRequired();
            builder.Property(w => w.Location).HasMaxLength(200).IsRequired();
            builder.Property(w => w.Email).HasMaxLength(200).IsRequired();
            builder.Property(w => w.PhoneNumber).HasMaxLength(50).IsRequired();
            builder.Property(w => w.HourlyRate).HasColumnType("decimal(18,2)");

            builder.HasOne(w => w.AssignedCaseManager)
                .WithMany()
                .HasForeignKey(w => w.AssignedCaseManagerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InsurancePolicy>(builder =>
        {
            builder.HasIndex(p => p.PolicyNumber).IsUnique();
            builder.Property(p => p.PolicyNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.Provider).HasMaxLength(200).IsRequired();
            builder.Property(p => p.CoverageType).HasMaxLength(100).IsRequired();
            builder.Property(p => p.CoverageAmount).HasColumnType("decimal(18,2)");

            builder.HasOne(p => p.Worker)
                .WithMany(w => w.Policies)
                .HasForeignKey(p => p.WorkerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Claim>(builder =>
        {
            builder.HasIndex(c => c.ClaimNumber).IsUnique();
            builder.Property(c => c.ClaimNumber).HasMaxLength(50).IsRequired();
            builder.Property(c => c.ClaimType).HasMaxLength(100).IsRequired();
            builder.Property(c => c.Description).HasMaxLength(2000).IsRequired();
            builder.Property(c => c.Amount).HasColumnType("decimal(18,2)");
            builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(c => c.ReviewedBy).HasMaxLength(200);
            builder.Property(c => c.AgentRecommendation).HasMaxLength(2000);
            builder.Property(c => c.Jurisdiction).HasMaxLength(10).IsRequired();

            builder.HasOne(c => c.Worker)
                .WithMany(w => w.Claims)
                .HasForeignKey(c => c.WorkerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PendingAction>(builder =>
        {
            builder.Property(a => a.ActionType).HasConversion<string>().HasMaxLength(30);
            builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(a => a.Payload).HasColumnType("nvarchar(max)");
            builder.Property(a => a.DecidedByRole).HasMaxLength(20);
            builder.Property(a => a.IdempotencyKey).HasMaxLength(100);
            builder.Property(a => a.ExecutionError).HasColumnType("nvarchar(max)");
            builder.Property(a => a.RuleOutputsJson).HasColumnType("nvarchar(max)");
            builder.HasIndex(a => a.IdempotencyKey);

            builder.HasOne(a => a.Claim)
                .WithMany(c => c.PendingActions)
                .HasForeignKey(a => a.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(a => a.ProposedByAgentRun)
                .WithMany()
                .HasForeignKey(a => a.ProposedByAgentRunId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AgentRunLog>(builder =>
        {
            builder.Property(r => r.Trigger).HasMaxLength(200).IsRequired();
            builder.Property(r => r.Prompt).HasColumnType("nvarchar(max)");
            builder.Property(r => r.ToolCallsJson).HasColumnType("nvarchar(max)");
            builder.Property(r => r.FinalAnswer).HasColumnType("nvarchar(max)");
            builder.Property(r => r.ModelId).HasMaxLength(100).IsRequired();
            builder.Property(r => r.ReasoningText).HasColumnType("nvarchar(max)");
            builder.Property(r => r.InputCost).HasColumnType("decimal(18,6)");
            builder.Property(r => r.OutputCost).HasColumnType("decimal(18,6)");
            builder.Property(r => r.TotalCost).HasColumnType("decimal(18,6)");
            builder.Property(r => r.Outcome).HasMaxLength(20).IsRequired();

            builder.HasOne(r => r.Claim)
                .WithMany()
                .HasForeignKey(r => r.ClaimId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasOne(r => r.ConversationSession)
                .WithMany()
                .HasForeignKey(r => r.ConversationSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationSession>(builder =>
        {
            builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            builder.Property(s => s.CreatedByRole).HasMaxLength(20).IsRequired();
            builder.Property(s => s.CreatedByName).HasMaxLength(200).IsRequired();
            builder.Property(s => s.Title).HasMaxLength(200);
            builder.Property(s => s.SerializedStateJson).HasColumnType("nvarchar(max)").IsRequired();
        });

        modelBuilder.Entity<WorkflowDefinition>(builder =>
        {
            builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
            builder.Property(w => w.Description).HasMaxLength(1000).IsRequired();
            builder.Property(w => w.InputSchemaJson).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(w => w.PromptTemplate).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(w => w.AllowedToolNamesJson).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(w => w.ChatTriggerHintsJson).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(w => w.CreatedByRole).HasMaxLength(20).IsRequired();

            // Phase 13 (docs/plan-agents.md §5): an ordered sequence of specialist-agent steps -
            // deleting the definition takes its steps with it, same as any other owned collection.
            builder.HasMany(w => w.Steps)
                .WithOne(s => s.WorkflowDefinition)
                .HasForeignKey(s => s.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStepDefinition>(builder =>
        {
            builder.Property(s => s.AgentName).HasMaxLength(100).IsRequired();
            builder.Property(s => s.PromptTemplate).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(s => s.OutputKey).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<WorkflowRun>(builder =>
        {
            builder.Property(r => r.InputValuesJson).HasColumnType("nvarchar(max)").IsRequired();
            builder.Property(r => r.TriggerSource).HasConversion<string>().HasMaxLength(20);
            builder.Property(r => r.RawChatInput).HasColumnType("nvarchar(max)");

            builder.HasOne(r => r.WorkflowDefinition)
                .WithMany()
                .HasForeignKey(r => r.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.AgentRunLog)
                .WithMany()
                .HasForeignKey(r => r.AgentRunLogId)
                .OnDelete(DeleteBehavior.Cascade);

            // Phase 13 (docs/plan-agents.md §5): one audit row per executed step for a multi-agent
            // run - deleting the run takes its step rows with it.
            builder.HasMany(r => r.Steps)
                .WithOne(s => s.WorkflowRun)
                .HasForeignKey(s => s.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowRunStep>(builder =>
        {
            builder.Property(s => s.AgentName).HasMaxLength(100).IsRequired();
            builder.Property(s => s.OutputKey).HasMaxLength(100).IsRequired();
            builder.Property(s => s.ResolvedPromptSnapshot).HasColumnType("nvarchar(max)").IsRequired();

            // Restrict, not Cascade: WorkflowRun already cascades from AgentRunLog above (via its
            // own AgentRunLogId FK) - a second cascade path from this FK to the same AgentRunLog
            // table would be a multiple-cascade-paths error (SQL Server error 1785, the same class
            // of error the User self-referencing FK hit in Phase 12, docs/plan.md §5). Moot in
            // practice anyway - AgentRunLog rows are audit records, never deleted.
            builder.HasOne(s => s.AgentRunLog)
                .WithMany()
                .HasForeignKey(s => s.AgentRunLogId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(builder =>
        {
            builder.HasIndex(u => u.Email).IsUnique();
            builder.Property(u => u.Email).HasMaxLength(200).IsRequired();
            builder.Property(u => u.Name).HasMaxLength(200).IsRequired();
            builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);

            // SQL Server rejects SET NULL/CASCADE on a self-referencing FK (error 1785) - Restrict
            // is the only option here anyway, since users are soft-deleted (IsActive) and never
            // hard-deleted, so this FK never actually needs to cascade or null itself out.
            builder.HasOne(u => u.CreatedByUser)
                .WithMany()
                .HasForeignKey(u => u.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
