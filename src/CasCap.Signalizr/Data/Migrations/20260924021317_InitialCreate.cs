using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: true),
                    sender = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<long>(type: "bigint", nullable: true),
                    persisted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriber_cursors",
                columns: table => new
                {
                    subscriber_name = table.Column<string>(type: "text", nullable: false),
                    last_acknowledged_message_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriber_cursors", x => x.subscriber_name);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_persisted_at_utc",
                table: "inbound_messages",
                column: "persisted_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_messages");

            migrationBuilder.DropTable(
                name: "subscriber_cursors");
        }
    }
}
