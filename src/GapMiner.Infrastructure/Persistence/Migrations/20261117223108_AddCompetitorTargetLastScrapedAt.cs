using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GapMiner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitorTargetLastScrapedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_scraped_at",
                table: "competitor_target",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_scraped_at",
                table: "competitor_target");
        }
    }
}
