using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060013_ProbeRegistry")]
public sealed class ProbeRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Which certificate (issuing CA hash + serial) may write as which site and sensor. A certificate row is never deleted and a
        -- disabled one never comes back: renewing means registering the new serial and disabling the old one, both audited.
        CREATE TABLE monitoring.probe_registry (
          issuer_sha256 char(64) NOT NULL CHECK (issuer_sha256 ~ '^[0-9A-F]{64}$'),
          serial_hex varchar(80) NOT NULL CHECK (serial_hex ~ '^[0-9A-F]+$'),
          site_id varchar(128) NOT NULL CHECK (length(site_id) > 0), sensor_id varchar(128) NOT NULL CHECK (length(sensor_id) > 0),
          active boolean NOT NULL DEFAULT true,
          registered_at timestamptz NOT NULL DEFAULT clock_timestamp(), disabled_at timestamptz NULL,
          PRIMARY KEY (issuer_sha256, serial_hex),
          CHECK (active = (disabled_at IS NULL))
        );
        CREATE INDEX probe_registry_identity ON monitoring.probe_registry(site_id, sensor_id);
        CREATE TABLE monitoring.probe_registry_audit (
          audit_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          action text NOT NULL CHECK (action IN ('registered','disabled')),
          issuer_sha256 char(64) NOT NULL, serial_hex varchar(80) NOT NULL, site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL,
          actor varchar(128) NOT NULL CHECK (length(btrim(actor)) > 0), reason varchar(512) NOT NULL CHECK (length(btrim(reason)) > 0)
        );
        CREATE FUNCTION monitoring.protect_probe_registry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' OR TG_TABLE_NAME = 'probe_registry_audit' THEN
            RAISE EXCEPTION 'the probe registry only grows and its audit trail is append-only' USING ERRCODE = '23000';
          END IF;
          IF NEW.issuer_sha256 <> OLD.issuer_sha256 OR NEW.serial_hex <> OLD.serial_hex OR NEW.site_id <> OLD.site_id
             OR NEW.sensor_id <> OLD.sensor_id OR (OLD.active = false AND NEW.active = true) THEN
            RAISE EXCEPTION 'a registered certificate keeps its binding and a disabled one is never re-enabled' USING ERRCODE = '23000';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER probe_registry_guard BEFORE UPDATE OR DELETE ON monitoring.probe_registry
          FOR EACH ROW EXECUTE FUNCTION monitoring.protect_probe_registry();
        CREATE TRIGGER probe_registry_audit_append_only BEFORE UPDATE OR DELETE ON monitoring.probe_registry_audit
          FOR EACH ROW EXECUTE FUNCTION monitoring.protect_probe_registry();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Registry rollback must preserve which certificates were ever authorized.");
}
