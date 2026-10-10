using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CasCap.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupNameToInboundMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "group_name",
                table: "inbound_messages",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("UPDATE inbound_messages SET group_name = channel;");

            // TODO: In the release after the GroupName-only writer has shipped and its rollback window has
            // closed, drop these synchronization objects and channel in a contract migration. Both remain
            // writable in this release to support rolling upgrades and rollback.
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    CREATE FUNCTION sync_inbound_message_group_name() RETURNS trigger AS $$
                    BEGIN
                        IF TG_OP = 'INSERT' THEN
                            NEW.group_name := COALESCE(NEW.group_name, NEW.channel);
                            NEW.channel := NEW.group_name;
                        ELSIF NEW.group_name IS DISTINCT FROM OLD.group_name THEN
                            NEW.channel := NEW.group_name;
                        ELSIF NEW.channel IS DISTINCT FROM OLD.channel THEN
                            NEW.group_name := NEW.channel;
                        END IF;
                        RETURN NEW;
                    END;
                    $$ LANGUAGE plpgsql;
                    CREATE TRIGGER sync_inbound_message_group_name
                    BEFORE INSERT OR UPDATE OF channel, group_name ON inbound_messages
                    FOR EACH ROW EXECUTE FUNCTION sync_inbound_message_group_name();
                    """);
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql("""
                    CREATE TRIGGER sync_inbound_message_group_name_insert
                    AFTER INSERT ON inbound_messages
                    WHEN NEW.channel IS NOT NEW.group_name
                    BEGIN
                        UPDATE inbound_messages
                        SET group_name = COALESCE(NEW.group_name, NEW.channel),
                            channel = COALESCE(NEW.group_name, NEW.channel)
                        WHERE id = NEW.id;
                    END;
                    CREATE TRIGGER sync_inbound_message_group_name_update
                    AFTER UPDATE OF channel, group_name ON inbound_messages
                    WHEN NEW.channel IS NOT NEW.group_name
                    BEGIN
                        UPDATE inbound_messages
                        SET group_name = CASE WHEN NEW.group_name IS NOT OLD.group_name THEN NEW.group_name ELSE NEW.channel END,
                            channel = CASE WHEN NEW.group_name IS NOT OLD.group_name THEN NEW.group_name ELSE NEW.channel END
                        WHERE id = NEW.id;
                    END;
                    """);
            }
            else
            {
                throw new System.NotSupportedException("Group-name migration requires PostgreSQL or SQLite.");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE inbound_messages SET channel = group_name;");
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    DROP TRIGGER sync_inbound_message_group_name ON inbound_messages;
                    DROP FUNCTION sync_inbound_message_group_name();
                    ALTER TABLE inbound_messages DROP COLUMN group_name;
                    """);
            }
            else if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql("""
                    DROP TRIGGER sync_inbound_message_group_name_insert;
                    DROP TRIGGER sync_inbound_message_group_name_update;
                    ALTER TABLE inbound_messages DROP COLUMN group_name;
                    """);
            }
            else
            {
                throw new System.NotSupportedException("Group-name migration requires PostgreSQL or SQLite.");
            }
        }
    }
}
