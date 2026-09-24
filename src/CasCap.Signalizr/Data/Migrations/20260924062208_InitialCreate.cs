using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channel = table.Column<string>(type: "text", nullable: true),
                    sender = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<long>(type: "bigint", nullable: true),
                    persisted_at_unix_milliseconds = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "inbound_attachments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    inbound_message_id = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: true),
                    filename = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_attachments", x => x.id);
                    table.ForeignKey(
                        name: "FK_inbound_attachments_inbound_messages_inbound_message_id",
                        column: x => x.inbound_message_id,
                        principalTable: "inbound_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_attachments_inbound_message_id",
                table: "inbound_attachments",
                column: "inbound_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_messages_persisted_at_unix_milliseconds",
                table: "inbound_messages",
                column: "persisted_at_unix_milliseconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_attachments");

            migrationBuilder.DropTable(
                name: "subscriber_cursors");

            migrationBuilder.DropTable(
                name: "inbound_messages");
        }
    }
}
