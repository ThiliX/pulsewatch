using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckStartedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_monitor_checks_started_at",
                table: "monitor_checks",
                column: "started_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitor_checks_started_at",
                table: "monitor_checks");
        }
    }
}
