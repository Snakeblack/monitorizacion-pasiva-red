using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060012_AccessDenialAudit")]
public sealed class AccessDenialAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Who was refused what and why, with a correlation id. Never a token, an address, an event or any other payload.
        CREATE TABLE monitoring.access_denial_audit (
          audit_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          subject text NULL, operation text NOT NULL CHECK (length(operation) <= 64),
          cause text NOT NULL CHECK (length(cause) <= 64), correlation_id text NOT NULL CHECK (length(correlation_id) <= 128)
        );
        CREATE INDEX access_denial_audit_time ON monitoring.access_denial_audit(occurred_at);
        CREATE FUNCTION monitoring.protect_access_denial_audit() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'the access denial audit trail is append-only' USING ERRCODE = '23000';
        END $$;
        CREATE TRIGGER access_denial_audit_append_only BEFORE UPDATE OR DELETE ON monitoring.access_denial_audit
          FOR EACH ROW EXECUTE FUNCTION monitoring.protect_access_denial_audit();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Audit rollback must preserve the denial trail.");
}
