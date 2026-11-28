using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVC.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloseResolvedDamageSessionsFromRepairResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH latest AS (
                    SELECT DISTINCT ON (s.id) s.id AS session_id, s.device_id, s.opened_at, i.id AS item_id,
                        i.execution_method, i.execution_status, i.result_recorded_at, i.result_recorded_by_account_id,
                        i.result_request_id, o.display_code
                    FROM device_damage_sessions s
                    JOIN service_order_items i ON i.damage_session_id = s.id
                    JOIN service_orders o ON o.id = i.service_order_id
                    WHERE s.closed_at IS NULL AND o.order_type = 'Repair' AND o.status <> 'Canceled'
                    ORDER BY s.id, o.created_at DESC, o.display_code DESC, i.id
                ), eligible AS (
                    SELECT latest.*, GREATEST(latest.opened_at, latest.result_recorded_at, returned.received_at) AS resolved_at
                    FROM latest JOIN devices d ON d.id = latest.device_id
                    LEFT JOIN LATERAL (
                        SELECT max(r.received_at) AS received_at
                        FROM export_slip_items e
                        JOIN export_return_slip_items ri ON ri.export_slip_item_id = e.id
                        JOIN export_return_slips r ON r.id = ri.return_slip_id
                        WHERE e.service_order_item_id = latest.item_id AND e.result = 'Success'
                    ) returned ON true
                    WHERE latest.execution_status = 'Completed' AND latest.result_recorded_at IS NOT NULL
                        AND latest.result_recorded_by_account_id IS NOT NULL
                        AND d.condition = 'DAMAGED'
                        AND (latest.execution_method <> 'External' OR returned.received_at IS NOT NULL)
                        AND NOT EXISTS (SELECT FROM damage_session_inspections x
                            WHERE x.service_order_item_id = latest.item_id AND NOT x.passed)
                        AND NOT EXISTS (SELECT FROM export_device_guards g WHERE g.device_id = d.id)
                        AND NOT EXISTS (SELECT FROM device_operation_holds h WHERE h.device_id = d.id)
                        AND NOT EXISTS (SELECT FROM service_order_device_holds h
                            WHERE h.device_id = d.id AND h.service_order_item_id <> latest.item_id)
                ), closed AS (
                    UPDATE device_damage_sessions s
                    SET closed_at = e.resolved_at, closed_by_account_id = e.result_recorded_by_account_id, version = s.version + 1
                    FROM eligible e WHERE s.id = e.session_id AND s.closed_at IS NULL
                    RETURNING s.device_id, e.session_id, e.item_id, e.result_request_id, e.display_code,
                        e.result_recorded_by_account_id, e.resolved_at
                ), restored AS (
                    UPDATE devices d SET condition = 'GOOD', version = d.version + 1
                    FROM closed c WHERE d.id = c.device_id
                    RETURNING c.*
                )
                INSERT INTO device_history (id, request_id, actor_account_id, action_code, outcome, occurred_at,
                    device_id, changed_fields, before_data, after_data)
                SELECT gen_random_uuid(), item_id, result_recorded_by_account_id, 'DEVICE_DAMAGE_RESOLVED', 'SUCCESS',
                    resolved_at, device_id, '["condition"]', '{"condition":"Damaged"}',
                    jsonb_build_object('condition', 'Good', 'sessionId', session_id, 'sourceCode', display_code,
                        'serviceOrderItemId', item_id, 'resultRequestId', result_request_id)
                FROM restored;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting code must not reopen reconciled sessions or remove their audit history.
        }
    }
}
