using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610050004_CanonicalOutbox")]
public sealed class CanonicalOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE monitoring.session_identity (
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
          document_key text NOT NULL UNIQUE, revision bigint NOT NULL CHECK(revision>0),
          state text NOT NULL CHECK(state IN ('active','deleted')), deleted_at timestamptz NULL,
          PRIMARY KEY(site_id,sensor_id,event_id),
          CHECK((state='active' AND deleted_at IS NULL) OR (state='deleted' AND deleted_at IS NOT NULL))
        );
        CREATE TABLE monitoring.session_metadata (
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
          started_at timestamptz NOT NULL, ended_at timestamptz NOT NULL CHECK(ended_at>=started_at),
          source_ip inet NOT NULL, destination_ip inet NOT NULL,
          source_port integer NOT NULL CHECK(source_port BETWEEN 0 AND 65535),
          destination_port integer NOT NULL CHECK(destination_port BETWEEN 0 AND 65535),
          protocol text NOT NULL CHECK(protocol IN ('TCP','UDP')), vlan_id integer NULL CHECK(vlan_id BETWEEN 0 AND 4094),
          provenance text NOT NULL CHECK(provenance IN ('synthetic','capture')), revision bigint NOT NULL CHECK(revision>0),
          inferred boolean NULL, partial boolean NULL, close_reason text NULL,
          packet_count bigint NULL CHECK(packet_count>=0), byte_count bigint NULL CHECK(byte_count>=0),
          PRIMARY KEY(site_id,sensor_id,event_id),
          FOREIGN KEY(site_id,sensor_id,event_id) REFERENCES monitoring.session_projection ON DELETE CASCADE,
          FOREIGN KEY(site_id,sensor_id,event_id) REFERENCES monitoring.session_identity ON DELETE RESTRICT,
          CHECK((provenance='synthetic' AND inferred IS NULL AND partial IS NULL AND close_reason IS NULL AND packet_count IS NULL AND byte_count IS NULL)
            OR (provenance='capture' AND inferred IS TRUE AND partial IS NOT NULL AND close_reason IN ('inactivity','max-duration','shutdown','restart') AND packet_count IS NOT NULL AND byte_count IS NOT NULL))
        );
        CREATE TABLE monitoring.projection_outbox (
          id uuid NOT NULL PRIMARY KEY, aggregateid text NOT NULL, aggregatetype text NOT NULL CHECK(aggregatetype='sessions'),
          target_topic text NOT NULL, revision bigint NOT NULL CHECK(revision>0), schema_version integer NOT NULL CHECK(schema_version=1),
          payload jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(aggregateid,revision,target_topic),
          FOREIGN KEY(aggregateid) REFERENCES monitoring.session_identity(document_key) ON DELETE RESTRICT
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Canonical rollback must preserve identities, revisions, sessions and publication outbox.");
}
