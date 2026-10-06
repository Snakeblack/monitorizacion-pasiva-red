using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060011_ManualInventory")]
public sealed class ManualInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- The confirmed inventory exists only through deliberate manual decisions.
        CREATE TABLE monitoring.device (
          device_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          name varchar(128) NOT NULL CHECK (length(btrim(name)) > 0),
          description varchar(1024) NOT NULL DEFAULT '',
          revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
        );
        -- A device is visible in every (site, sensor) scope it was created in or gained through a confirmed or merged candidate.
        CREATE TABLE monitoring.device_scope (
          device_id uuid NOT NULL REFERENCES monitoring.device(device_id) ON DELETE RESTRICT,
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL,
          PRIMARY KEY (device_id, site_id, sensor_id)
        );
        ALTER TABLE monitoring.device_candidate
          ADD CONSTRAINT device_candidate_device FOREIGN KEY (device_id) REFERENCES monitoring.device(device_id) ON DELETE RESTRICT;
        ALTER TABLE monitoring.device_candidate
          ADD CONSTRAINT device_candidate_state_matches_link CHECK ((state = 'confirmed') = (device_id IS NOT NULL));
        -- Append-only record of every manual change, written in the same transaction as the change.
        CREATE TABLE monitoring.inventory_audit (
          audit_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          actor text NOT NULL CHECK (length(btrim(actor)) > 0),
          action text NOT NULL CHECK (action IN ('device-created','device-updated','candidate-confirmed','candidate-rejected','candidate-merged')),
          device_id uuid NULL REFERENCES monitoring.device(device_id) ON DELETE RESTRICT,
          candidate_id uuid NULL REFERENCES monitoring.device_candidate(candidate_id) ON DELETE RESTRICT,
          device_revision_before bigint NULL, device_revision_after bigint NULL,
          candidate_revision_before bigint NULL, candidate_revision_after bigint NULL,
          changes jsonb NOT NULL DEFAULT '{}'::jsonb, reason text NULL
        );
        CREATE INDEX inventory_audit_device ON monitoring.inventory_audit(device_id, occurred_at) WHERE device_id IS NOT NULL;
        CREATE INDEX inventory_audit_candidate ON monitoring.inventory_audit(candidate_id, occurred_at) WHERE candidate_id IS NOT NULL;
        CREATE FUNCTION monitoring.protect_inventory_audit() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'the inventory audit trail is append-only' USING ERRCODE = '23000';
        END $$;
        CREATE TRIGGER inventory_audit_append_only BEFORE UPDATE OR DELETE ON monitoring.inventory_audit
          FOR EACH ROW EXECUTE FUNCTION monitoring.protect_inventory_audit();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Inventory rollback must preserve devices, decisions and the audit trail.");
}
