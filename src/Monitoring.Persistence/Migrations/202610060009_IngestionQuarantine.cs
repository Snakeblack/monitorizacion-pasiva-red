using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060009_IngestionQuarantine")]
public sealed class IngestionQuarantine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- An event is pending until it is processed or quarantined for a permanent cause; neither state is ever faked.
        ALTER TABLE monitoring.ingestion_inbox ADD COLUMN quarantined_at timestamptz NULL;
        ALTER TABLE monitoring.ingestion_inbox ADD CONSTRAINT ingestion_inbox_not_both_processed_and_quarantined
          CHECK (processed_at IS NULL OR quarantined_at IS NULL);
        -- The accepted event is evidence: its identity and content never change after acceptance.
        CREATE FUNCTION monitoring.protect_accepted_event() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.batch_id IS DISTINCT FROM OLD.batch_id OR NEW.schema_version IS DISTINCT FROM OLD.schema_version
             OR NEW.occurred_at IS DISTINCT FROM OLD.occurred_at OR NEW.occurred_at_text IS DISTINCT FROM OLD.occurred_at_text
             OR NEW.data IS DISTINCT FROM OLD.data THEN
            RAISE EXCEPTION 'an accepted ingestion event is immutable' USING ERRCODE = '23000';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER ingestion_inbox_immutable_content BEFORE UPDATE ON monitoring.ingestion_inbox
          FOR EACH ROW EXECUTE FUNCTION monitoring.protect_accepted_event();
        CREATE TABLE monitoring.ingestion_quarantine (
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
          cause text NOT NULL CHECK (cause IN ('contract-invalid','identity-suppressed','projection-conflict')),
          state text NOT NULL DEFAULT 'unresolved' CHECK (state IN ('unresolved','replaced','discarded')),
          attempts integer NOT NULL DEFAULT 1 CHECK (attempts >= 1),
          quarantined_at timestamptz NOT NULL DEFAULT clock_timestamp(), last_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          resolved_at timestamptz NULL, resolved_by text NULL, resolution_reason text NULL, replaced_by_event_id varchar(128) NULL,
          PRIMARY KEY (site_id, sensor_id, event_id),
          FOREIGN KEY (site_id, sensor_id, event_id) REFERENCES monitoring.ingestion_inbox(site_id, sensor_id, event_id) ON DELETE RESTRICT,
          CHECK ((state = 'unresolved' AND resolved_at IS NULL AND resolved_by IS NULL)
              OR (state <> 'unresolved' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND length(btrim(resolved_by)) > 0)),
          CHECK ((state = 'replaced') = (replaced_by_event_id IS NOT NULL))
        );
        CREATE INDEX ingestion_quarantine_unresolved ON monitoring.ingestion_quarantine(cause, quarantined_at) WHERE state = 'unresolved';
        -- Every state change is recorded once, in the same transaction as the change.
        CREATE TABLE monitoring.ingestion_quarantine_audit (
          audit_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
          action text NOT NULL CHECK (action IN ('quarantined','replaced','discarded')),
          cause text NOT NULL, actor text NOT NULL CHECK (length(btrim(actor)) > 0), reason text NULL,
          replaced_by_event_id varchar(128) NULL, occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          FOREIGN KEY (site_id, sensor_id, event_id) REFERENCES monitoring.ingestion_inbox(site_id, sensor_id, event_id) ON DELETE RESTRICT
        );
        CREATE INDEX ingestion_quarantine_audit_event ON monitoring.ingestion_quarantine_audit(site_id, sensor_id, event_id, occurred_at);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Quarantine rollback must preserve unresolved events, their evidence and the audit trail.");
}
