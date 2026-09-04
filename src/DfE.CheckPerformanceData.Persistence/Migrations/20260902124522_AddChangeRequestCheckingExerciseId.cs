using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestCheckingExerciseId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckingExerciseId",
                table: "ChangeRequests",
                type: "uuid",
                nullable: true);

            // Backfill before the foreign key goes on, so an existing row is not left saying only
            // which WINDOW it belongs to when the window runs two exercises on different dates.
            //
            // The CASE mirrors WhatToChangeCheckingExerciseMap exactly - it is the same mapping,
            // written in SQL because a migration cannot call into Application. Keep the two in step:
            // a new WhatToChange member that belongs to a results enquiry belongs in both.
            //
            // A ConfirmCorrect declaration has a null AmendmentType and still lands on PupilData via
            // the ELSE, which is correct - confirming the data is correct is a pupil-data action by
            // definition. A row whose window has no exercise of the mapped type simply matches
            // nothing and keeps its null, which is the same answer ICheckingExerciseService gives.
            migrationBuilder.Sql("""
                UPDATE "ChangeRequests" cr
                SET "CheckingExerciseId" = ce."Id"
                FROM "CheckingExercises" ce
                WHERE ce."CheckingWindowId" = cr."WindowId"
                  AND ce."ExerciseType" = CASE
                        WHEN cr."AmendmentType" IN ('IncorrectGrade', 'MissingQualification')
                            THEN 'ResultsEnquiry'
                        ELSE 'PupilData'
                      END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_CheckingExerciseId",
                table: "ChangeRequests",
                column: "CheckingExerciseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_CheckingExercises_CheckingExerciseId",
                table: "ChangeRequests",
                column: "CheckingExerciseId",
                principalTable: "CheckingExercises",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_CheckingExercises_CheckingExerciseId",
                table: "ChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_ChangeRequests_CheckingExerciseId",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "CheckingExerciseId",
                table: "ChangeRequests");
        }
    }
}
