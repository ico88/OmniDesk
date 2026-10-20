using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniDesk.Api.Data;

#nullable disable

namespace OmniDesk.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261020090000_TicketDeviceBindingIntegrity")]
public sealed class TicketDeviceBindingIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO "SupportTicketDevices"
                ("TicketId", "DeviceId", "LinkedAt", "DeviceNameSnapshot", "SiteIdSnapshot", "SiteNameSnapshot")
            SELECT t."Id", d."Id", t."CreatedAt", d."Name", d."SiteId", s."Name"
            FROM "SupportTickets" t
            JOIN "Devices" d ON d."Id" = t."DeviceId"
            JOIN "Sites" s ON s."Id" = d."SiteId"
            WHERE NOT EXISTS (
                SELECT 1 FROM "SupportTicketDevices" td WHERE td."TicketId" = t."Id"
            )
            ON CONFLICT ("TicketId", "DeviceId") DO NOTHING;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "SupportTickets" t
                    WHERE NOT EXISTS (SELECT 1 FROM "SupportTicketDevices" td WHERE td."TicketId" = t."Id")
                ) THEN
                    RAISE EXCEPTION 'Cannot add ticket-device integrity constraints: orphan tickets exist.';
                END IF;
            END $$;

            CREATE FUNCTION omnidesk_check_ticket_device_binding() RETURNS trigger
            LANGUAGE plpgsql AS $$
            DECLARE
                ticket_id uuid;
                ticket_ids uuid[];
            BEGIN
                IF TG_TABLE_NAME = 'SupportTickets' THEN
                    ticket_ids := ARRAY[NEW."Id"];
                ELSIF TG_OP = 'DELETE' THEN
                    ticket_ids := ARRAY[OLD."TicketId"];
                ELSIF TG_OP = 'UPDATE' THEN
                    ticket_ids := ARRAY[OLD."TicketId", NEW."TicketId"];
                ELSE
                    ticket_ids := ARRAY[NEW."TicketId"];
                END IF;

                FOREACH ticket_id IN ARRAY ticket_ids LOOP
                    IF EXISTS (SELECT 1 FROM "SupportTickets" t WHERE t."Id" = ticket_id)
                       AND NOT EXISTS (SELECT 1 FROM "SupportTicketDevices" td WHERE td."TicketId" = ticket_id) THEN
                        RAISE EXCEPTION 'Every support ticket must be linked to at least one device.'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_SupportTicket_HasDevice';
                    END IF;
                END LOOP;
                RETURN NULL;
            END $$;

            CREATE CONSTRAINT TRIGGER "CK_SupportTicket_HasDevice_OnTicket"
                AFTER INSERT ON "SupportTickets"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION omnidesk_check_ticket_device_binding();

            CREATE CONSTRAINT TRIGGER "CK_SupportTicket_HasDevice_OnDeviceLink"
                AFTER INSERT OR UPDATE OR DELETE ON "SupportTicketDevices"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION omnidesk_check_ticket_device_binding();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "CK_SupportTicket_HasDevice_OnDeviceLink" ON "SupportTicketDevices";
            DROP TRIGGER IF EXISTS "CK_SupportTicket_HasDevice_OnTicket" ON "SupportTickets";
            DROP FUNCTION IF EXISTS omnidesk_check_ticket_device_binding();
            """);
    }
}
