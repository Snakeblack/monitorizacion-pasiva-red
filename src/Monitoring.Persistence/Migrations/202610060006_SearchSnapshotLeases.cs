using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060006_SearchSnapshotLeases")]
public sealed class SearchSnapshotLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE monitoring.search_snapshot_lease (
          lease_id uuid NOT NULL PRIMARY KEY, subject text NOT NULL,
          pit_hash bytea NULL CHECK(pit_hash IS NULL OR octet_length(pit_hash)=32),
          pit_changes integer NOT NULL DEFAULT 0 CHECK(pit_changes>=0),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(), expires_at timestamptz NOT NULL
        );
        CREATE INDEX search_snapshot_lease_subject_expiry ON monitoring.search_snapshot_lease(subject,expires_at);
        CREATE INDEX search_snapshot_lease_expiry ON monitoring.search_snapshot_lease(expires_at);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE monitoring.search_snapshot_lease;");
}
