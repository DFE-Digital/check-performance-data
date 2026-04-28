using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace DfE.CheckPerformanceData.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class AddWikiPageSearch : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<string>(
				name: "BodyPlainText",
				table: "WikiPages",
				type: "text",
				nullable: false,
				defaultValue: "");

			// Backfill: strip HTML tags + collapse whitespace from existing sanitised Content.
			// Matches the C# StripTagsToPlainText(sanitisedHtml) behaviour — the source is
			// HtmlSanitizer output, so <script>/<style> are already gone. A Postgres regex is safe
			// here for the one-shot seed; new writes populate BodyPlainText via the service layer.
			migrationBuilder.Sql(@"
				UPDATE ""WikiPages""
				SET ""BodyPlainText"" = COALESCE(
					regexp_replace(
						regexp_replace(""Content"", '<[^>]*>', ' ', 'g'),
						'\s+', ' ', 'g'),
					'')
				WHERE ""Content"" IS NOT NULL;
			");

			migrationBuilder.AddColumn<NpgsqlTsVector>(
				name: "SearchVector",
				table: "WikiPages",
				type: "tsvector",
				nullable: false,
				computedColumnSql: "setweight(to_tsvector('english', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('english', coalesce(\"BodyPlainText\", '')), 'B')",
				stored: true);

			migrationBuilder.CreateIndex(
				name: "IX_WikiPages_SearchVector",
				table: "WikiPages",
				column: "SearchVector")
				.Annotation("Npgsql:IndexMethod", "gin");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "IX_WikiPages_SearchVector",
				table: "WikiPages");

			migrationBuilder.DropColumn(
				name: "SearchVector",
				table: "WikiPages");

			migrationBuilder.DropColumn(
				name: "BodyPlainText",
				table: "WikiPages");
		}
	}
}
