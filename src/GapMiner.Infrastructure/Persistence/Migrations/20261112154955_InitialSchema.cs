using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace GapMiner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "competitor_target",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    marketplace_kind = table.Column<string>(type: "text", nullable: false),
                    marketplace_url = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competitor_target", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_gap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competitor_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    detailed_description = table.Column<string>(type: "text", nullable: false),
                    severity_score = table.Column<double>(type: "double precision", nullable: false),
                    mention_frequency = table.Column<int>(type: "integer", nullable: false),
                    suggested_tech_stack = table.Column<string>(type: "text", nullable: false),
                    actionable_implementation_plan = table.Column<string>(type: "text", nullable: false),
                    identified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_gap", x => x.id);
                    table.ForeignKey(
                        name: "FK_feature_gap_competitor_target_competitor_target_id",
                        column: x => x.competitor_target_id,
                        principalTable: "competitor_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competitor_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    star_rating = table.Column<int>(type: "integer", nullable: false),
                    review_text = table.Column<string>(type: "text", nullable: false),
                    review_author = table.Column<string>(type: "text", nullable: false),
                    date_posted = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_review_id = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(3072)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review", x => x.id);
                    table.ForeignKey(
                        name: "FK_review_competitor_target_competitor_target_id",
                        column: x => x.competitor_target_id,
                        principalTable: "competitor_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_gap_competitor_target_id",
                table: "feature_gap",
                column: "competitor_target_id");

            // EF 8 encodes "every column descending" as an EMPTY bool array — not
            // all-false. MigrationRoundTripTests pins the emitted SQL via
            // pg_get_indexdef: (severity_score DESC, mention_frequency DESC).
            migrationBuilder.CreateIndex(
                name: "ix_feature_gap_severity_score_mention_frequency",
                table: "feature_gap",
                columns: new[] { "severity_score", "mention_frequency" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_review_competitor_target_id_source_review_id",
                table: "review",
                columns: new[] { "competitor_target_id", "source_review_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_gap");

            migrationBuilder.DropTable(
                name: "review");

            migrationBuilder.DropTable(
                name: "competitor_target");

            // Not scaffolded by EF: the vector extension was created by Up, and the
            // story's rollback validation requires `database update 0` to leave no
            // orphaned objects. Safe here — every table with a vector column is
            // already dropped above. Caveat: Up creates with IF NOT EXISTS (a
            // pre-existing extension survives apply) while this drop is
            // unconditional; extensions are database-scoped, and without CASCADE
            // this still fails loudly if any surviving object depends on it.
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector");
        }
    }
}
