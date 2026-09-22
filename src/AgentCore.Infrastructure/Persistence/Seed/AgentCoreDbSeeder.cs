using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgentCore.Infrastructure.Persistence.Seed;

/// <summary>
/// Fresh, self-contained sample data for AgentCore V2.0 — authored directly against the new
/// Domain entities, not copied or converted from `first-agent`'s seed data.
/// </summary>
public static class AgentCoreDbSeeder
{
    public static async Task SeedAsync(
        AgentCoreDbContext db,
        string superAdminEmail = "superadmin@agentcore.local",
        string superAdminPassword = "SuperAdmin123!",
        CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        await SeedWorkersAndClaimsAsync(db, ct);
        await SeedWorkflowDefinitionsAsync(db, ct);
        await SeedSuperAdminAsync(db, superAdminEmail, superAdminPassword, ct);
    }

    /// <summary>Own empty-table guard, independent of the Workers-empty check above (same
    /// pattern as SeedWorkflowDefinitionsAsync) so an already-populated database still gets a
    /// SuperAdmin on next startup. Email/password are demo defaults unless overridden by config
    /// (docs/plan.md section 5) - same "clearly-labeled demo value" spirit as the SQL `sa`
    /// password used elsewhere in this project.</summary>
    private static async Task SeedSuperAdminAsync(AgentCoreDbContext db, string email, string password, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var superAdmin = new User
        {
            Email = email,
            Name = "Super Admin",
            Role = UserRole.SuperAdmin,
            IsActive = true
        };
        superAdmin.PasswordHash = new PasswordHasher<User>().HashPassword(superAdmin, password);

        db.Users.Add(superAdmin);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedWorkersAndClaimsAsync(AgentCoreDbContext db, CancellationToken ct)
    {
        if (await db.Workers.AnyAsync(ct))
        {
            return;
        }

        var workers = new List<Worker>
        {
            new() { Code = "WRK-1001", Name = "Ava Thompson", Role = "Electrician", Location = "Sydney, AU", Email = "ava.thompson@agentcore.dev", PhoneNumber = "+61-2-5550-1001", HourlyRate = 58.50m, YearsOfExperience = 9, IsAvailable = true },
            new() { Code = "WRK-1002", Name = "Noah Kim", Role = "Plumber", Location = "Melbourne, AU", Email = "noah.kim@agentcore.dev", PhoneNumber = "+61-3-5550-1002", HourlyRate = 52.00m, YearsOfExperience = 6, IsAvailable = true },
            new() { Code = "WRK-1003", Name = "Mia Chen", Role = "Site Supervisor", Location = "Brisbane, AU", Email = "mia.chen@agentcore.dev", PhoneNumber = "+61-7-5550-1003", HourlyRate = 71.25m, YearsOfExperience = 14, IsAvailable = false },
            new() { Code = "WRK-1004", Name = "Liam Patel", Role = "Carpenter", Location = "Perth, AU", Email = "liam.patel@agentcore.dev", PhoneNumber = "+61-8-5550-1004", HourlyRate = 49.75m, YearsOfExperience = 4, IsAvailable = true },
            new() { Code = "WRK-1005", Name = "Zoe Nguyen", Role = "Welder", Location = "Adelaide, AU", Email = "zoe.nguyen@agentcore.dev", PhoneNumber = "+61-8-5550-1005", HourlyRate = 55.00m, YearsOfExperience = 8, IsAvailable = true },
            new() { Code = "WRK-1006", Name = "Ethan Brooks", Role = "Crane Operator", Location = "Sydney, AU", Email = "ethan.brooks@agentcore.dev", PhoneNumber = "+61-2-5550-1006", HourlyRate = 63.40m, YearsOfExperience = 11, IsAvailable = false },
        };

        await db.Workers.AddRangeAsync(workers, ct);
        await db.SaveChangesAsync(ct);

        var policies = new List<InsurancePolicy>
        {
            new() { WorkerId = workers[0].Id, PolicyNumber = "POL-9001", Provider = "SafeGuard Mutual", CoverageType = "Workers Compensation", CoverageAmount = 250000m, StartDate = new DateOnly(2023, 1, 1), EndDate = new DateOnly(2026, 12, 31), IsActive = true },
            new() { WorkerId = workers[1].Id, PolicyNumber = "POL-9002", Provider = "SafeGuard Mutual", CoverageType = "Workers Compensation", CoverageAmount = 200000m, StartDate = new DateOnly(2023, 3, 1), EndDate = new DateOnly(2027, 2, 28), IsActive = true },
            new() { WorkerId = workers[2].Id, PolicyNumber = "POL-9003", Provider = "NorthBridge Insurance", CoverageType = "Public Liability", CoverageAmount = 500000m, StartDate = new DateOnly(2022, 1, 1), EndDate = new DateOnly(2025, 1, 1), IsActive = false },
            new() { WorkerId = workers[3].Id, PolicyNumber = "POL-9004", Provider = "SafeGuard Mutual", CoverageType = "Workers Compensation", CoverageAmount = 150000m, StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2027, 1, 1), IsActive = true },
            new() { WorkerId = workers[4].Id, PolicyNumber = "POL-9005", Provider = "Coastal Underwriters", CoverageType = "Workers Compensation", CoverageAmount = 220000m, StartDate = new DateOnly(2023, 6, 1), EndDate = new DateOnly(2026, 6, 1), IsActive = true },
            new() { WorkerId = workers[5].Id, PolicyNumber = "POL-9006", Provider = "NorthBridge Insurance", CoverageType = "Public Liability", CoverageAmount = 400000m, StartDate = new DateOnly(2021, 1, 1), EndDate = new DateOnly(2024, 1, 1), IsActive = false },
        };

        await db.InsurancePolicies.AddRangeAsync(policies, ct);

        // IncidentDate/ReportedDate default to ClaimDate (reported and lodged the same day the
        // incident happened) unless noted otherwise below - this deliberately preserves the CV-1
        // known-bad rows documented in docs/business-logic.md §6 exactly as originally analyzed
        // (those were derived from ClaimDate vs policy period). Jurisdiction is derived from each
        // worker's seeded Location. One claim (CLM-250228-003) gets a genuine incident-to-report
        // lag so FR-1 has a real positive test case; it isn't one of the CV-1/CV-2 rows, so
        // shifting its IncidentDate away from ClaimDate doesn't disturb those known-bad claims.
        const string nsw = "NSW", vic = "VIC", qld = "QLD", wa = "WA", sa = "SA";

        var claims = new List<Claim>
        {
            new() { WorkerId = workers[0].Id, ClaimNumber = "CLM-230114-001", ClaimDate = new DateOnly(2023, 1, 14), IncidentDate = new DateOnly(2023, 1, 14), ReportedDate = new DateOnly(2023, 1, 14), Jurisdiction = nsw, ClaimType = "Injury", Amount = 3200m, Status = ClaimStatus.Approved, Description = "Minor electrical burn while rewiring a switchboard." },
            new() { WorkerId = workers[0].Id, ClaimNumber = "CLM-240609-002", ClaimDate = new DateOnly(2024, 6, 9), IncidentDate = new DateOnly(2024, 6, 9), ReportedDate = new DateOnly(2024, 6, 9), Jurisdiction = nsw, ClaimType = "Equipment Damage", Amount = 1150m, Status = ClaimStatus.Rejected, Description = "Damaged multimeter, no supporting incident report." },
            new() { WorkerId = workers[0].Id, ClaimNumber = "CLM-250228-003", ClaimDate = new DateOnly(2025, 2, 28), IncidentDate = new DateOnly(2025, 1, 19), ReportedDate = new DateOnly(2025, 2, 28), Jurisdiction = nsw, ClaimType = "Injury", Amount = 4800m, Status = ClaimStatus.UnderReview, Description = "Fall from ladder, back strain, awaiting physio report." },

            new() { WorkerId = workers[1].Id, ClaimNumber = "CLM-230322-004", ClaimDate = new DateOnly(2023, 3, 22), IncidentDate = new DateOnly(2023, 3, 22), ReportedDate = new DateOnly(2023, 3, 22), Jurisdiction = vic, ClaimType = "Property Damage", Amount = 2100m, Status = ClaimStatus.Approved, Description = "Burst pipe damaged client flooring during a repair job." },
            new() { WorkerId = workers[1].Id, ClaimNumber = "CLM-240715-005", ClaimDate = new DateOnly(2024, 7, 15), IncidentDate = new DateOnly(2024, 7, 15), ReportedDate = new DateOnly(2024, 7, 15), Jurisdiction = vic, ClaimType = "Medical", Amount = 950m, Status = ClaimStatus.Approved, Description = "Chemical exposure from drain cleaner, treated on-site." },
            new() { WorkerId = workers[1].Id, ClaimNumber = "CLM-260110-006", ClaimDate = new DateOnly(2026, 1, 10), IncidentDate = new DateOnly(2026, 1, 10), ReportedDate = new DateOnly(2026, 1, 10), Jurisdiction = vic, ClaimType = "Injury", Amount = 2600m, Status = ClaimStatus.Pending, Description = "Wrist strain from repetitive pipefitting work." },

            new() { WorkerId = workers[2].Id, ClaimNumber = "CLM-230502-007", ClaimDate = new DateOnly(2023, 5, 2), IncidentDate = new DateOnly(2023, 5, 2), ReportedDate = new DateOnly(2023, 5, 2), Jurisdiction = qld, ClaimType = "Liability", Amount = 18000m, Status = ClaimStatus.Approved, Description = "Third-party injury on site under supervisor's watch." },
            new() { WorkerId = workers[2].Id, ClaimNumber = "CLM-240118-008", ClaimDate = new DateOnly(2024, 1, 18), IncidentDate = new DateOnly(2024, 1, 18), ReportedDate = new DateOnly(2024, 1, 18), Jurisdiction = qld, ClaimType = "Property Damage", Amount = 5400m, Status = ClaimStatus.Rejected, Description = "Scaffolding collapse, investigation found no policy breach coverage." },
            new() { WorkerId = workers[2].Id, ClaimNumber = "CLM-250830-009", ClaimDate = new DateOnly(2025, 8, 30), IncidentDate = new DateOnly(2025, 8, 30), ReportedDate = new DateOnly(2025, 8, 30), Jurisdiction = qld, ClaimType = "Liability", Amount = 9200m, Status = ClaimStatus.UnderReview, Description = "Neighboring property fence damage during excavation oversight." },

            new() { WorkerId = workers[3].Id, ClaimNumber = "CLM-240305-010", ClaimDate = new DateOnly(2024, 3, 5), IncidentDate = new DateOnly(2024, 3, 5), ReportedDate = new DateOnly(2024, 3, 5), Jurisdiction = wa, ClaimType = "Injury", Amount = 1800m, Status = ClaimStatus.Approved, Description = "Splinter/laceration from table saw, minor first aid." },
            new() { WorkerId = workers[3].Id, ClaimNumber = "CLM-250612-011", ClaimDate = new DateOnly(2025, 6, 12), IncidentDate = new DateOnly(2025, 6, 12), ReportedDate = new DateOnly(2025, 6, 12), Jurisdiction = wa, ClaimType = "Equipment Damage", Amount = 760m, Status = ClaimStatus.Approved, Description = "Power tool damaged in transit to job site." },
            new() { WorkerId = workers[3].Id, ClaimNumber = "CLM-260305-012", ClaimDate = new DateOnly(2026, 3, 5), IncidentDate = new DateOnly(2026, 3, 5), ReportedDate = new DateOnly(2026, 3, 5), Jurisdiction = wa, ClaimType = "Injury", Amount = 2200m, Status = ClaimStatus.Pending, Description = "Finger injury during frame assembly." },

            new() { WorkerId = workers[4].Id, ClaimNumber = "CLM-230819-013", ClaimDate = new DateOnly(2023, 8, 19), IncidentDate = new DateOnly(2023, 8, 19), ReportedDate = new DateOnly(2023, 8, 19), Jurisdiction = sa, ClaimType = "Medical", Amount = 3400m, Status = ClaimStatus.Approved, Description = "Welding flash burn to eyes, urgent care visit." },
            new() { WorkerId = workers[4].Id, ClaimNumber = "CLM-240930-014", ClaimDate = new DateOnly(2024, 9, 30), IncidentDate = new DateOnly(2024, 9, 30), ReportedDate = new DateOnly(2024, 9, 30), Jurisdiction = sa, ClaimType = "Injury", Amount = 6100m, Status = ClaimStatus.Approved, Description = "Hand injury from grinder kickback." },
            new() { WorkerId = workers[4].Id, ClaimNumber = "CLM-250420-015", ClaimDate = new DateOnly(2025, 4, 20), IncidentDate = new DateOnly(2025, 4, 20), ReportedDate = new DateOnly(2025, 4, 20), Jurisdiction = sa, ClaimType = "Equipment Damage", Amount = 1400m, Status = ClaimStatus.Rejected, Description = "Welding rig malfunction attributed to poor maintenance." },
            new() { WorkerId = workers[4].Id, ClaimNumber = "CLM-260702-016", ClaimDate = new DateOnly(2026, 7, 2), IncidentDate = new DateOnly(2026, 7, 2), ReportedDate = new DateOnly(2026, 7, 2), Jurisdiction = sa, ClaimType = "Injury", Amount = 3900m, Status = ClaimStatus.UnderReview, Description = "Smoke inhalation in confined space welding job." },

            new() { WorkerId = workers[5].Id, ClaimNumber = "CLM-230228-017", ClaimDate = new DateOnly(2023, 2, 28), IncidentDate = new DateOnly(2023, 2, 28), ReportedDate = new DateOnly(2023, 2, 28), Jurisdiction = nsw, ClaimType = "Liability", Amount = 22000m, Status = ClaimStatus.Approved, Description = "Load swing caused damage to adjacent structure." },
            new() { WorkerId = workers[5].Id, ClaimNumber = "CLM-240814-018", ClaimDate = new DateOnly(2024, 8, 14), IncidentDate = new DateOnly(2024, 8, 14), ReportedDate = new DateOnly(2024, 8, 14), Jurisdiction = nsw, ClaimType = "Injury", Amount = 5200m, Status = ClaimStatus.Approved, Description = "Fall from crane cab during maintenance check." },
            new() { WorkerId = workers[5].Id, ClaimNumber = "CLM-250506-019", ClaimDate = new DateOnly(2025, 5, 6), IncidentDate = new DateOnly(2025, 5, 6), ReportedDate = new DateOnly(2025, 5, 6), Jurisdiction = nsw, ClaimType = "Property Damage", Amount = 14500m, Status = ClaimStatus.UnderReview, Description = "Crane boom contact damaged neighboring scaffolding." },
            new() { WorkerId = workers[5].Id, ClaimNumber = "CLM-260815-020", ClaimDate = new DateOnly(2026, 8, 15), IncidentDate = new DateOnly(2026, 8, 15), ReportedDate = new DateOnly(2026, 8, 15), Jurisdiction = nsw, ClaimType = "Injury", Amount = 3100m, Status = ClaimStatus.Pending, Description = "Back strain reported after extended shift in crane cab." },
        };

        await db.Claims.AddRangeAsync(claims, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Seeded independently of the workers/claims block above (its own empty-table
    /// guard, not gated on Workers being empty) so an already-populated database - e.g. this
    /// project's own long-running dev/compose database from before Phase 8 existed - still picks
    /// up these built-in workflows on next startup. docs/plan.md §11.</summary>
    private static async Task SeedWorkflowDefinitionsAsync(AgentCoreDbContext db, CancellationToken ct)
    {
        if (await db.WorkflowDefinitions.AnyAsync(ct))
        {
            return;
        }

        var definitions = new List<WorkflowDefinition>
        {
            new()
            {
                Name = "Process Claim",
                Description = "Runs the full deterministic claim-processing pipeline (coverage, escalation, risk) against one claim - equivalent to POST /api/agent/claims/{claimId}/process, which this workflow delegates to directly rather than a generic prompt template (claim processing has its own rule-integration a template can't express).",
                InputSchemaJson = """[{"name":"claimId","type":"int","required":true,"description":"The Id of the claim to process."}]""",
                PromptTemplate = "Process claim #{claimId} using the full deterministic rule pipeline (coverage, escalation, risk).",
                AllowedToolNamesJson = """["WorkerInformationFetcher","ClaimsSearcher","WorkerClaimsHistoryFetcher","CoverageChecker","EscalationEvaluator","ClaimRiskScorer","WorkerEmailSender","EscalationEmailSender","PayoutCalculator"]""",
                IsChatTriggerable = true,
                ChatTriggerHintsJson = """["process claim","evaluate claim","review claim","assess claim"]""",
                CreatedByRole = "Admin"
            },
            new()
            {
                Name = "Worker Claims History",
                Description = "Summarizes one worker's claims history, optionally bounded by a date range - read-only, no sensitive tools available.",
                InputSchemaJson = """[{"name":"workerId","type":"int","required":true,"description":"The Id of the worker."},{"name":"dateFrom","type":"date","required":false,"description":"Only include claims on or after this date."},{"name":"dateTo","type":"date","required":false,"description":"Only include claims on or before this date."}]""",
                PromptTemplate = "Summarize worker #{workerId}'s claims history (from {dateFrom} to {dateTo}). Report total claims, total amount, and any notable patterns (repeat claim types, clustering, gaps).",
                AllowedToolNamesJson = """["WorkerInformationFetcher","WorkerClaimsHistoryFetcher"]""",
                IsChatTriggerable = true,
                ChatTriggerHintsJson = """["claims history","worker history","how many claims","past claims"]""",
                CreatedByRole = "Admin"
            },
            new()
            {
                Name = "Escalate High-Value Claim",
                Description = "Assesses a claim for escalation and, if warranted, queues an escalation email only - PayoutCalculator and WorkerEmailSender are not in this workflow's tool set at all, so the model cannot call them regardless of what it decides.",
                InputSchemaJson = """[{"name":"claimId","type":"int","required":true,"description":"The Id of the claim to assess for escalation."}]""",
                PromptTemplate = "Assess claim #{claimId} using CoverageChecker and EscalationEvaluator; quote their results exactly. If escalation is warranted, queue an escalation email explaining why using EscalationEmailSender. Do not propose a payout or a worker email - this workflow only handles escalation.",
                AllowedToolNamesJson = """["WorkerInformationFetcher","ClaimsSearcher","WorkerClaimsHistoryFetcher","CoverageChecker","EscalationEvaluator","ClaimRiskScorer","EscalationEmailSender"]""",
                IsChatTriggerable = true,
                ChatTriggerHintsJson = """["escalate claim","escalate this claim","high value claim","flag for escalation"]""",
                CreatedByRole = "Admin"
            }
        };

        await db.WorkflowDefinitions.AddRangeAsync(definitions, ct);
        await db.SaveChangesAsync(ct);
    }
}
