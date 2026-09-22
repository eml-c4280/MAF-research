using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConversationSessionId",
                table: "AgentRunLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerializedStateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunLogs_ConversationSessionId",
                table: "AgentRunLogs",
                column: "ConversationSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRunLogs_ConversationSessions_ConversationSessionId",
                table: "AgentRunLogs",
                column: "ConversationSessionId",
                principalTable: "ConversationSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentRunLogs_ConversationSessions_ConversationSessionId",
                table: "AgentRunLogs");

            migrationBuilder.DropTable(
                name: "ConversationSessions");

            migrationBuilder.DropIndex(
                name: "IX_AgentRunLogs_ConversationSessionId",
                table: "AgentRunLogs");

            migrationBuilder.DropColumn(
                name: "ConversationSessionId",
                table: "AgentRunLogs");
        }
    }
}
