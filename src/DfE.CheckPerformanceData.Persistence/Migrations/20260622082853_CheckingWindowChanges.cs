using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CheckingWindowChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IngressFile",
                table: "CheckingWindows",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IngressFileChecksum",
                table: "CheckingWindows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Published",
                table: "CheckingWindows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SchemaFile",
                table: "CheckingWindows",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SchemaFileChecksum",
                table: "CheckingWindows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Validated_IngressValidationChecksum",
                table: "CheckingWindows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Validated_SchemaValidationChecksum",
                table: "CheckingWindows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Validated_ValidatedAt",
                table: "CheckingWindows",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IngressFile",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "IngressFileChecksum",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "Published",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "SchemaFile",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "SchemaFileChecksum",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "Validated_IngressValidationChecksum",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "Validated_SchemaValidationChecksum",
                table: "CheckingWindows");

            migrationBuilder.DropColumn(
                name: "Validated_ValidatedAt",
                table: "CheckingWindows");
        }
    }
}
