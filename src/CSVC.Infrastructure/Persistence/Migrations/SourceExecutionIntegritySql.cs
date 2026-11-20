namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class SourceExecutionIntegritySql
{
    // Retain every legacy export/return/hold check and change only how execution is authorized.
    internal static string LegacyFunction
    {
        get
        {
            var sql = ServiceExportReturnIntegritySql.Install;
            var start = sql.IndexOf("CREATE FUNCTION v9_check_order(", StringComparison.Ordinal);
            var end = sql.IndexOf("CREATE FUNCTION v9_check_affected_order(", start, StringComparison.Ordinal);
            return sql[start..end].Trim().Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal);
        }
    }
    internal static string Install => LegacyFunction
        .Replace("OR (o.status IN ('InProgress','Completed') AND (o.approved_at IS NULL OR o.approved_by_account_id IS NULL OR o.approval_request_id IS NULL))",
            "OR (o.source_request_id IS NULL AND o.status IN ('InProgress','Completed') AND (o.approved_at IS NULL OR o.approved_by_account_id IS NULL OR o.approval_request_id IS NULL))")
        .Replace("OR h.held_by_account_id<>o.approved_by_account_id OR h.held_at<>o.approved_at",
            "OR h.held_by_account_id IS DISTINCT FROM COALESCE(o.execution_started_by_account_id,o.approved_by_account_id) OR h.held_at IS DISTINCT FROM COALESCE(o.execution_started_at,o.approved_at)")
        .Replace("IF NOT FOUND THEN RETURN; END IF;", """
            IF NOT FOUND THEN RETURN; END IF;
            IF (o.source_request_id IS NULL AND (o.source_request_code IS NOT NULL OR o.execution_started_at IS NOT NULL OR o.execution_started_by_account_id IS NOT NULL))
              OR (o.source_request_id IS NOT NULL AND (o.order_type<>'Repair' OR o.status NOT IN ('InProgress','Completed')
                OR o.execution_started_at IS NULL OR o.execution_started_by_account_id IS DISTINCT FROM o.created_by_account_id
                OR o.approved_at IS NOT NULL OR o.approved_by_account_id IS NOT NULL OR o.approval_request_id IS NOT NULL
                OR NOT EXISTS(SELECT 1 FROM procurement_requests r WHERE r.id=o.source_request_id AND r.display_code=o.source_request_code AND r.request_type='Repair')
                OR NOT EXISTS(SELECT 1 FROM procurement_document_links l WHERE l.request_id=o.source_request_id AND l.kind='Repair' AND l.document_code=o.display_code))) THEN
              RAISE EXCEPTION 'Source execution must retain its original request and actual starter' USING ERRCODE='23514',CONSTRAINT='ck_source_execution';
            END IF;
            """) + """
        CREATE FUNCTION source_execution_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (NEW.source_request_id,NEW.source_request_code,NEW.execution_started_at,NEW.execution_started_by_account_id)
            IS DISTINCT FROM (OLD.source_request_id,OLD.source_request_code,OLD.execution_started_at,OLD.execution_started_by_account_id) THEN
            RAISE EXCEPTION 'Execution origin is immutable' USING ERRCODE='23514',CONSTRAINT='ck_source_execution_origin';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER source_execution_origin BEFORE UPDATE ON service_orders FOR EACH ROW EXECUTE FUNCTION source_execution_immutable();
        """;
    internal static string Remove => """
        DO $$ BEGIN
          IF EXISTS(SELECT 1 FROM service_orders WHERE source_request_id IS NOT NULL)
            OR EXISTS(SELECT 1 FROM procurement_request_events WHERE action='ASSIGNMENT_CHANGED') THEN
            RAISE EXCEPTION 'Downgrade would remove request responsibility or source execution history. Restore a verified pre-upgrade backup instead.';
          END IF;
        END $$;
        DROP TRIGGER source_execution_origin ON service_orders;
        DROP FUNCTION source_execution_immutable();
        """ + LegacyFunction;
}
