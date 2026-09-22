using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSafetyAndReliabilityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutionError",
                table: "PendingActions",
                type: "nvarchar(max)",
                nullable: true);

            // Auto-generated default was year-1 (default(DateTime)), which would make every
            // pre-existing AwaitingApproval row instantly "expired" the moment this migration
            // runs. Give existing rows a fresh 7-day grace period from migration time instead -
            // computed in SQL, not as a fixed C# literal baked in at generation time.
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "PendingActions",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "DATEADD(DAY, 7, SYSUTCDATETIME())");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "PendingActions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Auto-generated default was "", which doesn't match any of the values this column
            // is actually documented to hold. Existing completed runs have no recorded failure,
            // so "Success" (the entity's own default) is the correct backfill.
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AgentRunLogs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Success");

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_IdempotencyKey",
                table: "PendingActions",
                column: "IdempotencyKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingActions_IdempotencyKey",
                table: "PendingActions");

            migrationBuilder.DropColumn(
                name: "ExecutionError",
                table: "PendingActions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "PendingActions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "PendingActions");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AgentRunLogs");
        }
    }
}
