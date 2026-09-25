using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

[DbContext(typeof(MonitoringDbContext))]
[Migration("202609240002_DurableInbox")]
public sealed class DurableInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ingestion_origin",
            schema: "monitoring",
            columns: table => new
            {
                site_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                sensor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ingestion_origin", x => new { x.site_id, x.sensor_id });
            });

        migrationBuilder.CreateTable(
            name: "ingestion_inbox",
            schema: "monitoring",
            columns: table => new
            {
                site_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                sensor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                batch_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                schema_version = table.Column<short>(type: "smallint", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ingestion_inbox", x => new { x.site_id, x.sensor_id, x.event_id });
                table.ForeignKey(
                    name: "FK_ingestion_inbox_ingestion_origin_site_id_sensor_id",
                    columns: x => new { x.site_id, x.sensor_id },
                    principalSchema: "monitoring",
                    principalTable: "ingestion_origin",
                    principalColumns: new[] { "site_id", "sensor_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ingestion_inbox_site_id_sensor_id_accepted_at",
            schema: "monitoring",
            table: "ingestion_inbox",
            columns: new[] { "site_id", "sensor_id", "accepted_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ingestion_inbox", schema: "monitoring");
        migrationBuilder.DropTable(name: "ingestion_origin", schema: "monitoring");
    }
}
