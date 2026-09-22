using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessRuleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuleOutputsJson",
                table: "PendingActions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "IncidentDate",
                table: "Claims",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "Claims",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReportedDate",
                table: "Claims",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Auto-generated defaults above (year-1 dates, empty Jurisdiction) are wrong for any
            // Claims rows that already existed before this migration - AgentCoreDbSeeder only ever
            // populates a fresh, empty database, so an already-seeded environment (e.g. this
            // project's own long-running dev/compose database) would otherwise end up with every
            // existing claim spuriously failing CV-1 (incident date year 1 can't fall in any
            // policy's period) and Jurisdiction blank. Backfill to match exactly what
            // AgentCoreDbSeeder.cs itself sets for these fields (IncidentDate = ReportedDate =
            // ClaimDate, Jurisdiction derived from the worker's seeded Location), so pre-existing
            // rows end up identical to what a fresh seed would have produced. A no-op (0 rows
            // affected) on a genuinely empty/fresh database, since the seeder runs after migrations
            // there and inserts the correct values itself.
            migrationBuilder.Sql(
                """
                UPDATE c
                SET c.IncidentDate = c.ClaimDate,
                    c.ReportedDate = c.ClaimDate,
                    c.Jurisdiction = CASE
                        WHEN w.Location LIKE 'Sydney%' THEN 'NSW'
                        WHEN w.Location LIKE 'Melbourne%' THEN 'VIC'
                        WHEN w.Location LIKE 'Brisbane%' THEN 'QLD'
                        WHEN w.Location LIKE 'Perth%' THEN 'WA'
                        WHEN w.Location LIKE 'Adelaide%' THEN 'SA'
                        ELSE 'NSW'
                    END
                FROM Claims c
                INNER JOIN Workers w ON w.Id = c.WorkerId;
                """);

            // Matches AgentCoreDbSeeder.cs's one deliberate FR-1 test case (a genuine incident-to-
            // report lag) - CLM-250228-003 isn't one of the documented CV-1/CV-2 known-bad rows,
            // so shifting its IncidentDate doesn't disturb those.
            migrationBuilder.Sql(
                "UPDATE Claims SET IncidentDate = DATEADD(day, -40, ClaimDate) WHERE ClaimNumber = 'CLM-250228-003';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuleOutputsJson",
                table: "PendingActions");

            migrationBuilder.DropColumn(
                name: "IncidentDate",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "Jurisdiction",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "ReportedDate",
                table: "Claims");
        }
    }
}
