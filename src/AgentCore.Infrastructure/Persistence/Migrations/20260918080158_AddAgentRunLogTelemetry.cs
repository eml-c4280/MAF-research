using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRunLogTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InputCost",
                table: "AgentRunLogs",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokenCount",
                table: "AgentRunLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "AgentRunLogs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "OutputCost",
                table: "AgentRunLogs",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokenCount",
                table: "AgentRunLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningText",
                table: "AgentRunLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallCount",
                table: "AgentRunLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "AgentRunLogs",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokenCount",
                table: "AgentRunLogs",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputCost",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "InputTokenCount",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "OutputCost",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "OutputTokenCount",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "ReasoningText",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "ToolCallCount",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "TotalTokenCount",
                table: "AgentRunLogs");
        }
    }
}
