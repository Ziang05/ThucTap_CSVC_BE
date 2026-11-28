namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class ExportRecordingIntegritySql
{
    internal const string Install = """
        CREATE OR REPLACE FUNCTION v9_export_write_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE parent_id uuid;
        BEGIN
          IF TG_TABLE_NAME = 'export_slips' THEN
            IF TG_OP = 'INSERT' THEN
              IF NEW.status <> 'InProgress' OR NEW.approved_at IS NOT NULL
                OR NEW.approved_by_account_id IS NOT NULL OR NEW.approval_request_id IS NOT NULL OR NOT EXISTS (
                SELECT 1 FROM service_orders o WHERE o.id = NEW.service_order_id AND o.status = 'InProgress'
                AND ((o.source_request_id IS NULL AND o.approved_at IS NOT NULL AND o.approved_by_account_id IS NOT NULL
                    AND o.approval_request_id IS NOT NULL)
                  OR (o.source_request_id IS NOT NULL AND o.execution_started_at IS NOT NULL AND o.execution_started_by_account_id IS NOT NULL))
              ) THEN
                RAISE EXCEPTION 'Export requires an authorized service order in progress' USING ERRCODE='23514',CONSTRAINT='ck_export_source_authorized';
              END IF;
              RETURN NEW;
            END IF;
            IF TG_OP = 'DELETE' OR NEW.service_order_id <> OLD.service_order_id THEN
              RAISE EXCEPTION 'Export source and document are permanent' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_source_immutable';
            END IF;
            IF (NEW.approved_at,NEW.approved_by_account_id,NEW.approval_request_id)
              IS DISTINCT FROM (OLD.approved_at,OLD.approved_by_account_id,OLD.approval_request_id) THEN
              RAISE EXCEPTION 'Historical export approval is immutable' USING ERRCODE='23514',CONSTRAINT='ck_export_legacy_approval';
            END IF;
            IF ((NEW.facility_id,NEW.facility_name_snapshot,NEW.facility_address_snapshot,NEW.facility_contact_snapshot,NEW.planned_export_at,NEW.notes)
                IS DISTINCT FROM (OLD.facility_id,OLD.facility_name_snapshot,OLD.facility_address_snapshot,OLD.facility_contact_snapshot,OLD.planned_export_at,OLD.notes)
                OR (NEW.status = 'Canceled' AND OLD.status <> 'Canceled'))
              AND (OLD.status <> 'InProgress' OR EXISTS(SELECT 1 FROM export_slip_items l WHERE l.export_slip_id = OLD.id AND l.result IS NOT NULL)) THEN
              RAISE EXCEPTION 'Edit or cancel export before recording handover' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
            END IF;
            IF NEW.status <> OLD.status AND NOT (OLD.status = 'InProgress' AND NEW.status IN ('Completed','Canceled')) THEN
              RAISE EXCEPTION 'Invalid export transition' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_transition';
            END IF;
          ELSE
            IF TG_OP IN ('UPDATE','DELETE') AND OLD.result IS NOT NULL THEN
              RAISE EXCEPTION 'Final export result is immutable' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_result_immutable';
            END IF;
            IF TG_OP = 'UPDATE' AND (NEW.export_slip_id,NEW.service_order_item_id) IS DISTINCT FROM (OLD.export_slip_id,OLD.service_order_item_id) THEN
              RAISE EXCEPTION 'Export line ownership is permanent' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_source_immutable';
            END IF;
            IF TG_OP IN ('INSERT','DELETE') THEN
              parent_id := CASE WHEN TG_OP = 'DELETE' THEN OLD.export_slip_id ELSE NEW.export_slip_id END;
              IF NOT EXISTS(SELECT 1 FROM export_slips e WHERE e.id = parent_id AND e.status = 'InProgress'
                AND NOT EXISTS(SELECT 1 FROM export_slip_items l WHERE l.export_slip_id = e.id AND l.result IS NOT NULL)) THEN
                RAISE EXCEPTION 'Change export lines before recording handover' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
              END IF;
            ELSIF NEW.result IS NOT NULL AND NOT EXISTS(SELECT 1 FROM export_slips WHERE id = NEW.export_slip_id AND status IN ('InProgress','Completed')) THEN
              RAISE EXCEPTION 'Export must be open to record handover' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
            END IF;
          END IF;
          RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
        END $$;
        DROP TRIGGER v9_export_header_write ON export_slips;
        CREATE TRIGGER v9_export_header_write BEFORE INSERT OR UPDATE OR DELETE ON export_slips
            FOR EACH ROW EXECUTE FUNCTION v9_export_write_guard();
        """;

    internal static string Remove
    {
        get
        {
            var sql = ServiceExportReturnIntegritySql.Install;
            var start = sql.IndexOf("CREATE FUNCTION v9_export_write_guard(", StringComparison.Ordinal);
            var end = sql.IndexOf("CREATE TRIGGER v9_export_header_write", start, StringComparison.Ordinal);
            return """
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM export_slips e JOIN export_slip_items l ON l.export_slip_id = e.id
                      WHERE e.approved_at IS NULL AND l.result IS NOT NULL) THEN
                    RAISE EXCEPTION 'Downgrade would require inventing approval for recorded exports. Restore a pre-upgrade backup instead.';
                  END IF;
                END $$;
                DROP TRIGGER v9_export_header_write ON export_slips;
                UPDATE export_slips SET status = 'PendingApproval'
                WHERE status = 'InProgress' AND approved_at IS NULL;
                SET CONSTRAINTS ALL IMMEDIATE;
                """ + Environment.NewLine + sql[start..end].Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal) + """
                CREATE TRIGGER v9_export_header_write BEFORE UPDATE OR DELETE ON export_slips
                    FOR EACH ROW EXECUTE FUNCTION v9_export_write_guard();
                """;
        }
    }
}
