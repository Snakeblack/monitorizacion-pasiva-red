using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202609290003_SessionProjection")]
public sealed class SessionProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>("processed_at", "ingestion_inbox", "timestamp with time zone", schema: "monitoring", nullable: true);
        migrationBuilder.CreateTable(
            name: "session_projection", schema: "monitoring",
            columns: table => new
            {
                site_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                sensor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                occurred_at_text = table.Column<string>(type: "text", nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_session_projection", x => new { x.site_id, x.sensor_id, x.event_id });
                table.ForeignKey(name: "FK_session_projection_ingestion_inbox", columns: x => new { x.site_id, x.sensor_id, x.event_id },
                    principalSchema: "monitoring", principalTable: "ingestion_inbox", principalColumns: new[] { "site_id", "sensor_id", "event_id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ingestion_inbox_pending", "ingestion_inbox",
            new[] { "accepted_at", "site_id", "sensor_id", "event_id" }, schema: "monitoring", filter: "processed_at IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Session projection rollback must preserve accepted events, projections and processing marks.");
}
