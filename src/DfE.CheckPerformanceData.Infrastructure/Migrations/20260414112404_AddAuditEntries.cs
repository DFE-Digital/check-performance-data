using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DfE.CheckPerformanceData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WikiPages_WikiPages_ParentId",
                table: "WikiPages");

            migrationBuilder.DropForeignKey(
                name: "FK_WikiPageVersions_WikiPages_WikiPageId",
                table: "WikiPageVersions");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OldValues = table.Column<string>(type: "text", nullable: true),
                    NewValues = table.Column<string>(type: "text", nullable: true),
                    ChangedColumns = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType",
                table: "AuditEntries",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Timestamp",
                table: "AuditEntries",
                column: "Timestamp");

            migrationBuilder.AddForeignKey(
                name: "FK_WikiPage_WikiPage_ParentId",
                table: "WikiPages",
                column: "ParentId",
                principalTable: "WikiPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WikiPageVersion_WikiPage_WikiPageId",
                table: "WikiPageVersions",
                column: "WikiPageId",
                principalTable: "WikiPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WikiPage_WikiPage_ParentId",
                table: "WikiPages");

            migrationBuilder.DropForeignKey(
                name: "FK_WikiPageVersion_WikiPage_WikiPageId",
                table: "WikiPageVersions");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.AddForeignKey(
                name: "FK_WikiPages_WikiPages_ParentId",
                table: "WikiPages",
                column: "ParentId",
                principalTable: "WikiPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WikiPageVersions_WikiPages_WikiPageId",
                table: "WikiPageVersions",
                column: "WikiPageId",
                principalTable: "WikiPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
