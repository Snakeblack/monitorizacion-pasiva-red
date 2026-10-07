using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060014_RetentionIndexes")]
public sealed class RetentionIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Retention scans by age; without these every pass would read whole tables.
        CREATE INDEX session_metadata_ended_at ON monitoring.session_metadata(ended_at);
        CREATE INDEX session_identity_tombstones ON monitoring.session_identity(deleted_at) WHERE state='deleted';
        CREATE INDEX device_observation_observed_at ON monitoring.device_observation(observed_at);
        CREATE INDEX device_ip_association_last_seen ON monitoring.device_ip_association(last_seen);
        CREATE INDEX ingestion_inbox_processed_expiry ON monitoring.ingestion_inbox(occurred_at) WHERE processed_at IS NOT NULL;
        CREATE INDEX projection_outbox_created_at ON monitoring.projection_outbox(created_at);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX monitoring.projection_outbox_created_at, monitoring.ingestion_inbox_processed_expiry, monitoring.device_ip_association_last_seen,
          monitoring.device_observation_observed_at, monitoring.session_identity_tombstones, monitoring.session_metadata_ended_at;
        """);
}
