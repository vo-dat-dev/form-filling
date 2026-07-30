using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProverbsAgent.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Form",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    fields = table.Column<string>(type: "jsonb", nullable: false),
                    embedding = table.Column<string>(type: "vector(1024)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Thread",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agentId = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false, defaultValue: "New Conversation"),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Thread", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "FormSubmission",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    formId = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmission", x => x.id);
                    table.ForeignKey(
                        name: "FK_FormSubmission_Form_formId",
                        column: x => x.formId,
                        principalTable: "Form",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmission_formId",
                table: "FormSubmission",
                column: "formId");

            migrationBuilder.CreateIndex(
                name: "IX_Thread_agentId",
                table: "Thread",
                column: "agentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormSubmission");

            migrationBuilder.DropTable(
                name: "Thread");

            migrationBuilder.DropTable(
                name: "Form");
        }
    }
}
