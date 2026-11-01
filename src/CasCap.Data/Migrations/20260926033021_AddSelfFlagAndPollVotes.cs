using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfFlagAndPollVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "from_self",
                table: "inbound_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "poll_vote_option_indexes",
                table: "inbound_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "poll_vote_timestamp",
                table: "inbound_messages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "from_self",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "poll_vote_option_indexes",
                table: "inbound_messages");

            migrationBuilder.DropColumn(
                name: "poll_vote_timestamp",
                table: "inbound_messages");
        }
    }
}
