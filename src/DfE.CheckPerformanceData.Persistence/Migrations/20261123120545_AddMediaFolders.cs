using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                table: "media_assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, computedColumnSql: "lower(name)", stored: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_folders", x => x.id);
                    table.CheckConstraint("ck_media_folders_name_not_blank", "btrim(name) <> ''");
                    table.CheckConstraint("ck_media_folders_not_own_parent", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_media_folders_media_folders_parent_id",
                        column: x => x.parent_id,
                        principalTable: "media_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_folder_id",
                table: "media_assets",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ux_media_folders_parent_id_name_key",
                table: "media_folders",
                columns: new[] { "parent_id", "name_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "fk_media_assets_media_folders_folder_id",
                table: "media_assets",
                column: "folder_id",
                principalTable: "media_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A move must not put a folder inside itself. The model cannot describe a trigger, so
            // it is SQL. The advisory lock makes two moves wait for each other: two editors who
            // each move one folder into the other cannot both pass the check.
            migrationBuilder.Sql(@"
CREATE FUNCTION media_folders_refuse_cycle() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.parent_id IS NULL OR NEW.parent_id IS NOT DISTINCT FROM OLD.parent_id THEN
        RETURN NEW;
    END IF;
    PERFORM pg_advisory_xact_lock(hashtext('media_folders_tree'));
    IF EXISTS (
        WITH RECURSIVE above AS (
            SELECT id, parent_id FROM media_folders WHERE id = NEW.parent_id
            UNION
            SELECT f.id, f.parent_id FROM media_folders f JOIN above a ON f.id = a.parent_id)
        SELECT 1 FROM above WHERE id = NEW.id) THEN
        RAISE EXCEPTION 'A media folder cannot move inside itself'
            USING ERRCODE = 'check_violation', CONSTRAINT = 'ck_media_folders_no_cycle';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER trg_media_folders_refuse_cycle
    BEFORE UPDATE OF parent_id ON media_folders
    FOR EACH ROW EXECUTE FUNCTION media_folders_refuse_cycle();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_media_folders_refuse_cycle ON media_folders;
DROP FUNCTION IF EXISTS media_folders_refuse_cycle();");

            migrationBuilder.DropForeignKey(
                name: "fk_media_assets_media_folders_folder_id",
                table: "media_assets");

            migrationBuilder.DropTable(
                name: "media_folders");

            migrationBuilder.DropIndex(
                name: "ix_media_assets_folder_id",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "media_assets");
        }
    }
}
