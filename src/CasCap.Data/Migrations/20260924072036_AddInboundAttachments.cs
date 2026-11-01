using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_attachments");
        }
    }
}
