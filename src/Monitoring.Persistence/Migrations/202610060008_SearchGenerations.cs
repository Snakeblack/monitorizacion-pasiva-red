using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060008_SearchGenerations")]
public sealed class SearchGenerations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- A rebuilt generation re-publishes the authority's latest records to its own topic; source_outbox_id links each copy
        -- to the ordinary row it came from, so concurrent changes are copied exactly once by an anti-join, never by ordering.
        ALTER TABLE monitoring.projection_outbox ADD COLUMN source_outbox_id uuid NULL;
        CREATE UNIQUE INDEX projection_outbox_generation_source
          ON monitoring.projection_outbox(source_outbox_id,target_topic) WHERE source_outbox_id IS NOT NULL;
        CREATE TABLE monitoring.search_generation (
          generation integer NOT NULL PRIMARY KEY CHECK(generation>=1),
          index_name text NOT NULL UNIQUE, target_topic text NOT NULL UNIQUE,
          state text NOT NULL CHECK(state IN ('building','ready','active','retired','aborted')),
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(), snapshot_deadline timestamptz NOT NULL,
          copied bigint NOT NULL DEFAULT 0 CHECK(copied>=0), activated_at timestamptz NULL
        );
        CREATE UNIQUE INDEX search_generation_one_active ON monitoring.search_generation((true)) WHERE state='active';
        -- Generation 1 is the index and topic the pipeline already feeds.
        INSERT INTO monitoring.search_generation(generation,index_name,target_topic,state,snapshot_deadline,activated_at)
          VALUES (1,'sessions-v2-000001','monitoring.sessions.v2','active',clock_timestamp(),clock_timestamp());
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Generation rollback must preserve the registry and publication history.");
}
