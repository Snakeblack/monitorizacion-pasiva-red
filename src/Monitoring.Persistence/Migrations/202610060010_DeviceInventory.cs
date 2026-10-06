using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060010_DeviceInventory")]
public sealed class DeviceInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Observed facts, kept apart from any confirmed inventory. One row per accepted event identity.
        CREATE TABLE monitoring.device_observation (
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
          observed_at timestamptz NOT NULL, ip inet NOT NULL, mac macaddr NULL,
          vlan_id integer NULL CHECK (vlan_id BETWEEN 0 AND 4094),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY (site_id, sensor_id, event_id),
          FOREIGN KEY (site_id, sensor_id, event_id) REFERENCES monitoring.ingestion_inbox(site_id, sensor_id, event_id) ON DELETE RESTRICT
        );
        CREATE INDEX device_observation_mac ON monitoring.device_observation(site_id, sensor_id, mac, observed_at) WHERE mac IS NOT NULL;
        -- A candidate is identified by MAC + site + sensor + VLAN, where "no VLAN tag" is its own scope. Nothing merges or
        -- confirms candidates automatically: not an equal IP, an equal name, NAT or a randomized MAC.
        CREATE TABLE monitoring.device_candidate (
          candidate_id uuid NOT NULL PRIMARY KEY DEFAULT gen_random_uuid(),
          site_id varchar(128) NOT NULL, sensor_id varchar(128) NOT NULL, mac macaddr NOT NULL,
          vlan_id integer NULL CHECK (vlan_id BETWEEN 0 AND 4094),
          first_seen timestamptz NOT NULL, last_seen timestamptz NOT NULL CHECK (last_seen >= first_seen),
          state text NOT NULL DEFAULT 'candidate' CHECK (state IN ('candidate','confirmed','rejected')),
          revision bigint NOT NULL DEFAULT 1 CHECK (revision > 0),
          device_id uuid NULL
        );
        CREATE UNIQUE INDEX device_candidate_identity
          ON monitoring.device_candidate(site_id, sensor_id, mac, (COALESCE(vlan_id, -1)));
        -- Temporary IP associations of a candidate: the period each address was observed for it, with no owner inferred.
        CREATE TABLE monitoring.device_ip_association (
          candidate_id uuid NOT NULL REFERENCES monitoring.device_candidate(candidate_id) ON DELETE RESTRICT,
          ip inet NOT NULL, first_seen timestamptz NOT NULL, last_seen timestamptz NOT NULL CHECK (last_seen >= first_seen),
          PRIMARY KEY (candidate_id, ip)
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Inventory rollback must preserve observations, candidates and their decisions.");
}
