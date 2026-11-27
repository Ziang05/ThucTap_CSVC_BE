using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CSVC.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CsvcDbContext))]
[Migration("20261127020000_PurchaseRequestLifecycle")]
public sealed class PurchaseRequestLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            WITH progress AS (
                SELECT r.id, r.status,
                    coalesce((SELECT e.actor_account_id FROM procurement_request_events e WHERE e.request_id = r.id
                        ORDER BY e.occurred_at DESC, e.id DESC LIMIT 1), r.assignee_account_id, r.assigned_by_account_id) AS context_actor_id,
                    NOT EXISTS (SELECT 1 FROM procurement_request_lines l WHERE l.request_id = r.id AND l.kind <> 'Equipment') AS equipment_only,
                    EXISTS (
                        SELECT 1 FROM procurement_document_links l JOIN device_transfers t ON t.transfer_code = l.document_code
                        WHERE l.request_id = r.id AND l.kind = 'Allocation' AND upper(t.status::text) = 'COMPLETED'
                    ) OR EXISTS (SELECT 1 FROM procurement_document_links l WHERE l.request_id = r.id AND l.kind = 'InternalUse') AS received,
                    EXISTS (
                        SELECT 1 FROM procurement_document_links l JOIN device_transfers t ON t.transfer_code = l.document_code
                        WHERE l.request_id = r.id AND l.kind = 'Allocation'
                            AND upper(t.status::text) IN ('PENDING_CONFIRMATION','PENDING_APPROVAL','APPROVED','COMPLETED')
                    ) AS allocated,
                    EXISTS (
                        SELECT 1 FROM procurement_document_links l JOIN device_receipts d ON d.receipt_code = l.document_code
                        WHERE l.request_id = r.id AND l.kind = 'Receipt' AND upper(d.status::text) = 'COMPLETED'
                    ) AS stocked,
                    EXISTS (
                        SELECT 1 FROM procurement_document_links l JOIN device_receipts d ON d.receipt_code = l.document_code
                        WHERE l.request_id = r.id AND l.kind = 'Receipt' AND upper(d.status::text) IN ('PENDING_APPROVAL','APPROVED')
                    ) OR EXISTS (SELECT 1 FROM procurement_request_events e WHERE e.request_id = r.id AND e.action = 'PURCHASE_TRACKING_STARTED') AS tracking,
                    r.requires_review, r.mapping_issue,
                    (SELECT max(t.completed_at) FROM procurement_document_links l JOIN device_transfers t ON t.transfer_code = l.document_code
                        WHERE l.request_id = r.id AND l.kind = 'Allocation' AND upper(t.status::text) = 'COMPLETED') AS received_at
                FROM procurement_requests r
                WHERE r.request_type = 'Purchase' AND r.status NOT IN ('Completed','Cancelled')
                    AND EXISTS (SELECT 1 FROM procurement_request_lines l WHERE l.request_id = r.id AND l.kind = 'Equipment')
            ), planned AS (
                SELECT p.*, CASE
                    WHEN received AND equipment_only AND NOT requires_review AND mapping_issue IS NULL THEN 'Completed'
                    WHEN allocated OR received THEN 'AwaitingReceipt'
                    WHEN stocked THEN 'AwaitingHandover'
                    WHEN tracking OR status IN ('TrackingPurchase','WaitingForGoods','WaitingForExternal') THEN 'TrackingPurchase'
                    ELSE 'Received' END AS next_status
                FROM progress p
            ), changed AS (
                UPDATE procurement_requests r SET status = p.next_status, version = r.version + 1,
                    result = CASE WHEN p.next_status = 'Completed' THEN 'Success' ELSE r.result END,
                    closed_at = CASE WHEN p.next_status = 'Completed' THEN coalesce(p.received_at, CURRENT_TIMESTAMP) ELSE r.closed_at END
                FROM planned p WHERE r.id = p.id AND r.status <> p.next_status AND p.context_actor_id IS NOT NULL
                RETURNING r.id, r.status, p.context_actor_id
            )
            INSERT INTO procurement_request_events (id, request_id, action, outcome, occurred_at, actor_account_id, reason)
            SELECT gen_random_uuid(), id, 'PURCHASE_LIFECYCLE_RECONCILED', status, CURRENT_TIMESTAMP, context_actor_id,
                'Trạng thái được hệ thống đồng bộ từ chứng từ; tài khoản tham chiếu thuộc lịch sử xử lý đã có.'
            FROM changed;
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE procurement_requests SET status = 'InProgress', version = version + 1
            WHERE status = 'AwaitingReceipt';
            """);
}
