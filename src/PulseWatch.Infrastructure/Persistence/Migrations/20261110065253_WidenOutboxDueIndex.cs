using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenOutboxDueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_outbox_due",
                table: "notification_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_due",
                table: "notification_outbox",
                column: "next_attempt_at_utc",
                filter: "status IN (0, 1, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_outbox_due",
                table: "notification_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_due",
                table: "notification_outbox",
                column: "next_attempt_at_utc",
                filter: "status IN (0, 3)");
        }
    }
}
