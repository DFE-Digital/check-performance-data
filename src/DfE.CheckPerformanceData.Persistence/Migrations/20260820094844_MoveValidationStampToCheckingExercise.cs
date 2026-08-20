using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <summary>
    /// Moves the validation stamp from the checking window down to the checking exercise (#319).
    /// A window is no longer validated as a whole: each exercise has its own inputs, on its own
    /// dates, so one window-level flag could only ever describe one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing is backfilled, on purpose.</b> Both <c>CreateAsync</c> and <c>UpdateAsync</c>
    /// wrote <c>Validated</c> unconditionally, so every window on every environment carries a stamp
    /// whether or not anything was ever validated. Copying that onto the exercises would mark the
    /// whole estate validated on the strength of a value that recorded nothing. Exercises start
    /// unvalidated and an admin revalidates — the fail-closed answer, and the only honest one.
    /// </para>
    /// <para>
    /// Written as idempotent SQL rather than the scaffolded <c>AddColumn</c>/<c>DropColumn</c> pair
    /// so it no-ops on a database that already has the change, per the reconciliation rule in
    /// CLAUDE.md.
    /// </para>
    /// </remarks>
    public partial class MoveValidationStampToCheckingExercise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CheckingExercises"
                    ADD COLUMN IF NOT EXISTS "Validated_ValidatedAt" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "Validated_IngressValidationChecksum" character varying(256) NULL,
                    ADD COLUMN IF NOT EXISTS "Validated_SchemaValidationChecksum" character varying(256) NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CheckingWindows"
                    DROP COLUMN IF EXISTS "Validated_ValidatedAt",
                    DROP COLUMN IF EXISTS "Validated_IngressValidationChecksum",
                    DROP COLUMN IF EXISTS "Validated_SchemaValidationChecksum";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CheckingWindows"
                    ADD COLUMN IF NOT EXISTS "Validated_ValidatedAt" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "Validated_IngressValidationChecksum" character varying(256) NULL,
                    ADD COLUMN IF NOT EXISTS "Validated_SchemaValidationChecksum" character varying(256) NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CheckingExercises"
                    DROP COLUMN IF EXISTS "Validated_ValidatedAt",
                    DROP COLUMN IF EXISTS "Validated_IngressValidationChecksum",
                    DROP COLUMN IF EXISTS "Validated_SchemaValidationChecksum";
                """);
        }
    }
}
