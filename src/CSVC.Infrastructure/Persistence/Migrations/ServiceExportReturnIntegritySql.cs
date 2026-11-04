namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class ServiceExportReturnIntegritySql
{
    internal const string Prepare = """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM service_orders) OR EXISTS (SELECT 1 FROM export_slips)
             OR EXISTS (SELECT 1 FROM device_operation_holds WHERE operation_kind = 'ServiceOrder') THEN
            RAISE EXCEPTION 'V9 requires empty legacy service/export data. Verify a disposable test environment and explicitly reset/reseed it before retrying. No data was changed.';
          END IF;
        END $$;
        DROP TRIGGER ck_export_line_membership ON export_slip_items;
        DROP TRIGGER ck_service_item_export_membership ON service_order_items;
        DROP TRIGGER ck_export_header_membership ON export_slips;
        DROP FUNCTION check_service_export_membership();
        """;

    internal const string Install = """
        CREATE FUNCTION v9_immutable_record() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Confirmed documents and audit are immutable'
          USING ERRCODE='23514', CONSTRAINT='ck_v9_immutable'; END $$;
        CREATE TRIGGER v9_return_header_immutable BEFORE UPDATE OR DELETE ON export_return_slips
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();
        CREATE TRIGGER v9_return_item_immutable BEFORE UPDATE OR DELETE ON export_return_slip_items
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();
        CREATE TRIGGER v9_return_history_immutable BEFORE UPDATE OR DELETE ON export_return_history
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();
        CREATE TRIGGER v9_export_history_immutable BEFORE UPDATE OR DELETE ON export_history
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();
        CREATE TRIGGER v9_service_history_immutable BEFORE UPDATE OR DELETE ON service_order_history
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();

        CREATE TRIGGER v9_hold_owner_immutable BEFORE UPDATE ON service_order_device_holds
          FOR EACH ROW EXECUTE FUNCTION v9_immutable_record();
        CREATE FUNCTION v9_guard_owner_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (NEW.device_id,NEW.export_slip_item_id,NEW.acquired_at) IS DISTINCT FROM (OLD.device_id,OLD.export_slip_item_id,OLD.acquired_at)
             OR (OLD.guard_state='AwaitingReturn' AND NEW.guard_state<>'AwaitingReturn') THEN
            RAISE EXCEPTION 'Guard ownership is immutable' USING ERRCODE='23514',CONSTRAINT='ck_v9_guard_owner';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER v9_guard_owner BEFORE UPDATE ON export_device_guards FOR EACH ROW EXECUTE FUNCTION v9_guard_owner_immutable();
        CREATE FUNCTION v9_source_write_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME='service_orders' THEN
            IF (NEW.id,NEW.order_type,NEW.create_request_id,NEW.create_request_hash,NEW.created_by_account_id,NEW.created_at,NEW.display_code)
              IS DISTINCT FROM (OLD.id,OLD.order_type,OLD.create_request_id,OLD.create_request_hash,OLD.created_by_account_id,OLD.created_at,OLD.display_code)
              OR (OLD.status<>'PendingApproval' AND NEW.notes IS DISTINCT FROM OLD.notes) THEN
              RAISE EXCEPTION 'Approved source configuration is immutable' USING ERRCODE='23514',CONSTRAINT='ck_v9_source_configuration';
            END IF;
            IF OLD.status<>NEW.status AND NOT ((OLD.status='PendingApproval' AND NEW.status IN ('InProgress','Canceled'))
              OR (OLD.status='InProgress' AND NEW.status='Completed')) THEN
              RAISE EXCEPTION 'Invalid source transition' USING ERRCODE='23514',CONSTRAINT='ck_v9_source_transition';
            END IF;
          ELSE
            IF OLD.execution_status IS NOT NULL AND (NEW.service_order_id,NEW.device_id,NEW.item_no,NEW.execution_method,NEW.work_description,NEW.notes,
                NEW.device_code_snapshot,NEW.device_name_snapshot,NEW.serial_snapshot)
              IS DISTINCT FROM (OLD.service_order_id,OLD.device_id,OLD.item_no,OLD.execution_method,OLD.work_description,OLD.notes,
                OLD.device_code_snapshot,OLD.device_name_snapshot,OLD.serial_snapshot) THEN
              RAISE EXCEPTION 'Approved source items are immutable' USING ERRCODE='23514',CONSTRAINT='ck_v9_source_configuration';
            END IF;
            IF OLD.execution_status IN ('Completed','Failed','NotExecuted') AND
              (NEW.execution_status,NEW.result_description,NEW.result_recorded_by_account_id,NEW.result_recorded_at,NEW.result_request_id)
              IS DISTINCT FROM (OLD.execution_status,OLD.result_description,OLD.result_recorded_by_account_id,OLD.result_recorded_at,OLD.result_request_id) THEN
              RAISE EXCEPTION 'Professional result is final' USING ERRCODE='23514',CONSTRAINT='ck_v9_source_result';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER v9_source_header_write BEFORE UPDATE ON service_orders FOR EACH ROW EXECUTE FUNCTION v9_source_write_guard();
        CREATE TRIGGER v9_source_item_write BEFORE UPDATE ON service_order_items FOR EACH ROW EXECUTE FUNCTION v9_source_write_guard();

        CREATE FUNCTION v9_export_write_guard() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_TABLE_NAME = 'export_slips' THEN
            IF TG_OP = 'DELETE' OR NEW.service_order_id <> OLD.service_order_id THEN
              RAISE EXCEPTION 'Export source and document are permanent' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_source_immutable';
            END IF;
            IF (NEW.facility_id,NEW.facility_name_snapshot,NEW.facility_address_snapshot,NEW.facility_contact_snapshot,
                NEW.planned_export_at,NEW.notes) IS DISTINCT FROM
               (OLD.facility_id,OLD.facility_name_snapshot,OLD.facility_address_snapshot,OLD.facility_contact_snapshot,
                OLD.planned_export_at,OLD.notes) AND OLD.status <> 'PendingApproval' THEN
              RAISE EXCEPTION 'Only pending export can be edited' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
            END IF;
            IF NEW.status <> OLD.status AND NOT ((OLD.status='PendingApproval' AND NEW.status IN ('InProgress','Canceled'))
              OR (OLD.status='InProgress' AND NEW.status='Completed')) THEN
              RAISE EXCEPTION 'Invalid export transition' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_transition';
            END IF;
          ELSIF TG_TABLE_NAME = 'export_slip_items' THEN
            IF TG_OP IN ('UPDATE','DELETE') AND OLD.result IS NOT NULL THEN
              RAISE EXCEPTION 'Final export result is immutable' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_result_immutable';
            END IF;
            IF TG_OP='UPDATE' AND (NEW.export_slip_id,NEW.service_order_item_id) IS DISTINCT FROM (OLD.export_slip_id,OLD.service_order_item_id) THEN
              RAISE EXCEPTION 'Export line ownership is permanent' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_source_immutable';
            END IF;
            IF TG_OP='DELETE' THEN
              IF NOT EXISTS(SELECT 1 FROM export_slips WHERE id=OLD.export_slip_id AND status='PendingApproval')
                  THEN
                RAISE EXCEPTION 'Removing a line needs pending status and an EDIT snapshot' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
              END IF;
            ELSIF TG_OP='INSERT' AND NOT EXISTS(SELECT 1 FROM export_slips WHERE id=NEW.export_slip_id AND status='PendingApproval') THEN
              RAISE EXCEPTION 'Lines may only be added to pending export' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
            ELSIF TG_OP='UPDATE' AND NEW.result IS NOT NULL AND NOT EXISTS(SELECT 1 FROM export_slips WHERE id=NEW.export_slip_id AND status IN ('InProgress','Completed')) THEN
              RAISE EXCEPTION 'Approve export before recording results' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_pending';
            END IF;
          END IF;
          RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;
        CREATE TRIGGER v9_export_header_write BEFORE UPDATE OR DELETE ON export_slips FOR EACH ROW EXECUTE FUNCTION v9_export_write_guard();
        CREATE TRIGGER v9_export_line_write BEFORE INSERT OR UPDATE OR DELETE ON export_slip_items FOR EACH ROW EXECUTE FUNCTION v9_export_write_guard();

        CREATE FUNCTION v9_check_order(p_order uuid) RETURNS void LANGUAGE plpgsql AS $$
        DECLARE o service_orders%ROWTYPE;
        BEGIN
          SELECT * INTO o FROM service_orders WHERE id=p_order;
          IF NOT FOUND THEN RETURN; END IF;
          IF NOT EXISTS(SELECT 1 FROM service_order_items WHERE service_order_id=p_order)
            OR (o.status IN ('PendingApproval','Canceled') AND EXISTS(SELECT 1 FROM service_order_items WHERE service_order_id=p_order AND execution_status IS NOT NULL))
            OR (o.status IN ('InProgress','Completed') AND (o.approved_at IS NULL OR o.approved_by_account_id IS NULL OR o.approval_request_id IS NULL))
            OR (o.status='InProgress' AND (NOT EXISTS(SELECT 1 FROM service_order_items WHERE service_order_id=p_order AND execution_status='InProgress')
                 OR EXISTS(SELECT 1 FROM service_order_items WHERE service_order_id=p_order AND execution_status IS NULL)))
            OR (o.status='Completed' AND (o.completed_at IS NULL OR EXISTS(SELECT 1 FROM service_order_items WHERE service_order_id=p_order AND (execution_status IS NULL OR execution_status='InProgress')))) THEN
            RAISE EXCEPTION 'Source status does not match all source items' USING ERRCODE='23514',CONSTRAINT='ck_v9_service_completion';
          END IF;
          IF EXISTS(SELECT 1 FROM service_order_items i LEFT JOIN service_order_device_holds h
              ON (h.service_order_item_id,h.service_order_id,h.device_id)=(i.id,i.service_order_id,i.device_id)
              WHERE i.service_order_id=p_order AND i.execution_status='InProgress' AND h.device_id IS NULL)
            OR EXISTS(SELECT 1 FROM service_order_device_holds h JOIN service_order_items i ON i.id=h.service_order_item_id
              WHERE h.service_order_id=p_order AND (i.execution_status IS NULL OR o.status IN ('PendingApproval','Canceled')
                OR h.held_by_account_id<>o.approved_by_account_id OR h.held_at<>o.approved_at)) THEN
            RAISE EXCEPTION 'Source hold ownership is invalid' USING ERRCODE='23514',CONSTRAINT='ck_v9_service_hold';
          END IF;
          IF EXISTS(SELECT 1 FROM export_slips e WHERE e.service_order_id=p_order AND (
            NOT EXISTS(SELECT 1 FROM export_slip_items l WHERE l.export_slip_id=e.id)
            OR (e.status='Completed') IS DISTINCT FROM (NOT EXISTS(SELECT 1 FROM export_slip_items l WHERE l.export_slip_id=e.id AND l.result IS NULL))
            OR (e.status IN ('PendingApproval','Canceled') AND EXISTS(SELECT 1 FROM export_slip_items l WHERE l.export_slip_id=e.id AND l.result IS NOT NULL)))) THEN
            RAISE EXCEPTION 'Export completion requires all item results' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_completion';
          END IF;
          IF EXISTS(SELECT 1 FROM export_slips e JOIN export_slip_items l ON l.export_slip_id=e.id
            JOIN service_order_items i ON i.id=l.service_order_item_id
            LEFT JOIN export_device_guards g ON g.export_slip_item_id=l.id
            LEFT JOIN export_return_slip_items r ON r.export_slip_item_id=l.id
            WHERE e.service_order_id=p_order AND (
              i.service_order_id<>e.service_order_id OR i.execution_method<>'External'
              OR (g.device_id IS NOT NULL AND g.device_id<>i.device_id)
              OR (l.result IS NULL AND e.status<>'Canceled' AND
                (g.device_id IS NULL OR g.guard_state<>'HeldForExport' OR i.execution_status<>'InProgress' OR o.status<>'InProgress'))
              OR (l.result='Success' AND r.id IS NULL AND (g.device_id IS NULL OR g.guard_state<>'AwaitingReturn'))
              OR ((e.status='Canceled' OR l.result='Failed' OR r.id IS NOT NULL) AND g.device_id IS NOT NULL)
              OR (r.id IS NOT NULL AND l.result IS DISTINCT FROM 'Success'))) THEN
            RAISE EXCEPTION 'Export ownership, result, return or guard does not match' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_guard';
          END IF;
          IF EXISTS(SELECT 1 FROM export_device_guards g JOIN export_slip_items l ON l.id=g.export_slip_item_id
            JOIN service_order_items i ON i.id=l.service_order_item_id
            LEFT JOIN service_order_device_holds h ON (h.device_id,h.service_order_item_id)=(i.device_id,i.id)
            WHERE i.service_order_id=p_order AND h.device_id IS NULL) THEN
            RAISE EXCEPTION 'Active export requires the matching service hold' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_service_hold';
          END IF;
          IF EXISTS(SELECT 1 FROM export_return_slips r JOIN export_slips e ON e.id=r.export_slip_id
            WHERE e.service_order_id=p_order AND (NOT EXISTS(SELECT 1 FROM export_return_slip_items ri WHERE ri.return_slip_id=r.id)
              OR r.received_at>clock_timestamp()
              OR NOT EXISTS(SELECT 1 FROM export_return_history h WHERE h.return_slip_id=r.id AND h.return_slip_item_id IS NULL AND h.action_code='RETURN_SLIP_RECORDED')
              OR EXISTS(SELECT 1 FROM export_return_slip_items ri WHERE ri.return_slip_id=r.id AND NOT EXISTS(
                SELECT 1 FROM export_return_history h WHERE h.return_slip_id=r.id AND h.return_slip_item_id=ri.id AND h.action_code='RETURN_ITEM_RECEIVED')))) THEN
            RAISE EXCEPTION 'Confirmed return requires nonempty items and immutable audit' USING ERRCODE='23514',CONSTRAINT='ck_v9_return_audit';
          END IF;
        END $$;

        CREATE FUNCTION v9_check_affected_order() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rowdata jsonb; order_id uuid; export_id uuid;
        BEGIN
          rowdata := CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          IF TG_TABLE_NAME='service_orders' THEN order_id := (rowdata->>'id')::uuid;
          ELSIF TG_TABLE_NAME IN ('service_order_items','service_order_device_holds') THEN order_id := (rowdata->>'service_order_id')::uuid;
          ELSIF TG_TABLE_NAME='export_slips' THEN order_id := (rowdata->>'service_order_id')::uuid;
          ELSIF TG_TABLE_NAME='export_device_guards' THEN
            SELECT i.service_order_id INTO order_id FROM export_slip_items l JOIN service_order_items i ON i.id=l.service_order_item_id WHERE l.id=(rowdata->>'export_slip_item_id')::uuid;
            IF order_id IS NULL THEN SELECT service_order_id INTO order_id FROM service_order_device_holds WHERE device_id=(rowdata->>'device_id')::uuid; END IF;
          ELSE
            export_id := (rowdata->>'export_slip_id')::uuid;
            SELECT service_order_id INTO order_id FROM export_slips WHERE id=export_id;
          END IF;
          IF TG_TABLE_NAME='service_order_device_holds' THEN
            IF TG_OP='DELETE' AND (EXISTS(SELECT 1 FROM service_order_items WHERE id=OLD.service_order_item_id AND (execution_status IS NULL OR execution_status='InProgress'))
              OR EXISTS(SELECT 1 FROM export_device_guards WHERE device_id=OLD.device_id)
              OR NOT EXISTS(SELECT 1 FROM service_order_history WHERE service_order_item_id=OLD.service_order_item_id AND action_code='HOLD_RELEASED')) THEN
              RAISE EXCEPTION 'Release requires a terminal item, no export guard, and HOLD_RELEASED audit' USING ERRCODE='23514',CONSTRAINT='ck_v9_hold_release';
            ELSIF TG_OP='INSERT' AND EXISTS(SELECT 1 FROM device_operation_holds WHERE device_id=NEW.device_id) THEN
              RAISE EXCEPTION 'Conflicting operation hold' USING ERRCODE='23514',CONSTRAINT='ck_v9_cross_hold';
            END IF;
          END IF;
          IF TG_TABLE_NAME='export_slip_items' AND TG_OP='DELETE' AND NOT EXISTS(
            SELECT 1 FROM export_history WHERE export_slip_id=(rowdata->>'export_slip_id')::uuid AND action_code='EDIT'
              AND before_data->'items' @> jsonb_build_array(jsonb_build_object('lineId',(rowdata->>'id')::uuid))) THEN
            RAISE EXCEPTION 'Deleted line requires an immutable EDIT snapshot' USING ERRCODE='23514',CONSTRAINT='ck_v9_export_edit_audit';
          END IF;
          PERFORM v9_check_order(order_id);
          RETURN NULL;
        END $$;
        DO $$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['service_orders','service_order_items','service_order_device_holds','export_slips','export_slip_items','export_device_guards','export_return_slips','export_return_slip_items'] LOOP
            EXECUTE format('CREATE CONSTRAINT TRIGGER v9_consistency AFTER INSERT OR UPDATE OR DELETE ON %I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v9_check_affected_order()',t);
          END LOOP;
        END $$;
        """;

    internal const string Remove = """
        DO $$ DECLARE t text; BEGIN
          IF EXISTS(SELECT 1 FROM service_orders) OR EXISTS(SELECT 1 FROM export_slips) THEN
            RAISE EXCEPTION 'V9 downgrade requires empty service/export data; no automatic destructive reset is permitted';
          END IF;
          FOREACH t IN ARRAY ARRAY['service_orders','service_order_items','service_order_device_holds','export_slips','export_slip_items','export_device_guards','export_return_slips','export_return_slip_items'] LOOP
            EXECUTE format('DROP TRIGGER v9_consistency ON %I',t);
          END LOOP;
        END $$;
        DROP TRIGGER v9_return_header_immutable ON export_return_slips;
        DROP TRIGGER v9_return_item_immutable ON export_return_slip_items;
        DROP TRIGGER v9_return_history_immutable ON export_return_history;
        DROP TRIGGER v9_export_history_immutable ON export_history;
        DROP TRIGGER v9_service_history_immutable ON service_order_history;
        DROP TRIGGER v9_export_header_write ON export_slips;
        DROP TRIGGER v9_export_line_write ON export_slip_items;
        DROP TRIGGER v9_hold_owner_immutable ON service_order_device_holds;
        DROP TRIGGER v9_guard_owner ON export_device_guards;
        DROP TRIGGER v9_source_header_write ON service_orders;
        DROP TRIGGER v9_source_item_write ON service_order_items;
        DROP FUNCTION v9_guard_owner_immutable();
        DROP FUNCTION v9_source_write_guard();
        DROP FUNCTION v9_check_affected_order();
        DROP FUNCTION v9_check_order(uuid);
        DROP FUNCTION v9_export_write_guard();
        DROP FUNCTION v9_immutable_record();
        """;
}
