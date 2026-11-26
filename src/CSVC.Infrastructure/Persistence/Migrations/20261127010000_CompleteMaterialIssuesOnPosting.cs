using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CsvcDbContext))]
[Migration("20261127010000_CompleteMaterialIssuesOnPosting")]
public sealed class CompleteMaterialIssuesOnPosting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            -- Existing issues have already changed stock. Complete them without posting again
            -- or inventing a receipt acknowledgement. Mixed equipment deliveries keep their workflow.
            ALTER TABLE material_documents DISABLE TRIGGER immutable_posted_material_document;
            WITH completed AS (
                UPDATE material_documents AS d
                SET status = 'Completed', version = version + 1
                WHERE d.kind = 'Issue' AND d.status = 'AwaitingReceipt'
                    AND d.posted_at IS NOT NULL AND d.posted_by_account_id IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM procurement_request_lines AS l
                        WHERE l.request_id = d.procurement_request_id AND l.kind = 'Equipment'
                    )
                RETURNING d.id, d.posted_by_account_id, d.version
            )
            INSERT INTO material_document_history
                (id, document_id, actor_account_id, action, reason, before_data, after_data, occurred_at)
            SELECT gen_random_uuid(), id, posted_by_account_id, 'RECEIPT_STEP_REMOVED',
                'System migration: receipt confirmation removed for posted material-only issues; stock unchanged.',
                jsonb_build_object('Status', 'AwaitingReceipt', 'Version', version - 1),
                jsonb_build_object('Status', 'Completed', 'Version', version), CURRENT_TIMESTAMP
            FROM completed;
            ALTER TABLE material_documents ENABLE TRIGGER immutable_posted_material_document;
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Completed business documents and their audit evidence remain intact on rollback.
    }
}
