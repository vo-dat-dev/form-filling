using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace ProverbsAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentChunking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Document",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    fileName = table.Column<string>(type: "text", nullable: false),
                    mediaType = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Chunk",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    documentId = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    start_at = table.Column<int>(type: "integer", nullable: false),
                    end_at = table.Column<int>(type: "integer", nullable: false),
                    parent_chunk_id = table.Column<string>(type: "text", nullable: true),
                    chunk_type = table.Column<string>(type: "text", nullable: false, defaultValue: "text"),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chunk", x => x.id);
                    table.ForeignKey(
                        name: "FK_Chunk_Chunk_parent_chunk_id",
                        column: x => x.parent_chunk_id,
                        principalTable: "Chunk",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chunk_Document_documentId",
                        column: x => x.documentId,
                        principalTable: "Document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chunk_documentId",
                table: "Chunk",
                column: "documentId");

            migrationBuilder.CreateIndex(
                name: "IX_Chunk_parent_chunk_id",
                table: "Chunk",
                column: "parent_chunk_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Chunk");

            migrationBuilder.DropTable(
                name: "Document");
        }
    }
}
