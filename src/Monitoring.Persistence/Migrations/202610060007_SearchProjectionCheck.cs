using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060007_SearchProjectionCheck")]
public sealed class SearchProjectionCheck : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE monitoring.search_projection_check (
          check_id uuid NOT NULL PRIMARY KEY,
          started_at timestamptz NOT NULL, finished_at timestamptz NULL,
          upper_bound timestamptz NOT NULL,
          examined bigint NOT NULL DEFAULT 0, missing bigint NOT NULL DEFAULT 0, stale bigint NOT NULL DEFAULT 0,
          max_lag_seconds bigint NULL, complete boolean NOT NULL DEFAULT false,
          CHECK(NOT complete OR finished_at IS NOT NULL)
        );
        CREATE INDEX search_projection_check_complete ON monitoring.search_projection_check(finished_at DESC) WHERE complete;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE monitoring.search_projection_check;");
}
