using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Monitoring.Persistence.Migrations;

// Elasticsearch rejects an _id over 512 bytes while a valid identity encodes to up to 515, so the index keys on a compact,
// authority-assigned UUID instead. The full triple and document_key stay the identity of record.
[DbContext(typeof(MonitoringDbContext))]
[Migration("202610060005_SearchDocumentIdentity")]
public sealed class SearchDocumentIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- The volatile default is evaluated per row, so existing identities each receive their own persistent value.
        ALTER TABLE monitoring.session_identity ADD COLUMN search_document_id uuid NOT NULL DEFAULT gen_random_uuid();
        ALTER TABLE monitoring.session_identity
          ADD CONSTRAINT session_identity_search_document_id_key UNIQUE (search_document_id);
        CREATE FUNCTION monitoring.protect_search_document_id() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'search_document_id is permanent once assigned' USING ERRCODE = '23000';
        END $$;
        CREATE TRIGGER session_identity_search_document_id_immutable
          BEFORE UPDATE OF search_document_id ON monitoring.session_identity
          FOR EACH ROW WHEN (NEW.search_document_id IS DISTINCT FROM OLD.search_document_id)
          EXECUTE FUNCTION monitoring.protect_search_document_id();
        -- Schema 1 history stays append-only; schema 2 adds searchDocumentId and is published on a new topic.
        ALTER TABLE monitoring.projection_outbox DROP CONSTRAINT projection_outbox_schema_version_check;
        ALTER TABLE monitoring.projection_outbox
          ADD CONSTRAINT projection_outbox_schema_version_check CHECK (schema_version IN (1,2));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Search identity rollback must preserve assigned identifiers and publication history.");
}
