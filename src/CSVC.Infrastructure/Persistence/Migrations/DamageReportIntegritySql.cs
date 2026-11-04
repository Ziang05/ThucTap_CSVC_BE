namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class DamageReportIntegritySql
{
    public const string Up = """
ALTER TABLE damage_report_pending_devices ADD CONSTRAINT fk_damage_pending_report_device
    FOREIGN KEY (damage_report_id, device_id) REFERENCES damage_reports(id, device_id)
    DEFERRABLE INITIALLY DEFERRED;

CREATE FUNCTION damage_report_integrity() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE report_id uuid; r damage_reports; pending_count integer; reception_count integer;
BEGIN
    IF TG_TABLE_NAME = 'damage_reports' THEN report_id := COALESCE(NEW.id, OLD.id);
    ELSE report_id := COALESCE(NEW.damage_report_id, OLD.damage_report_id); END IF;
    SELECT * INTO r FROM damage_reports WHERE id = report_id;
    IF NOT FOUND THEN RETURN NULL; END IF;
    SELECT count(*) INTO pending_count FROM damage_report_pending_devices WHERE damage_report_id = report_id AND device_id = r.device_id;
    SELECT count(*) INTO reception_count FROM damage_report_receptions WHERE damage_report_id = report_id AND received_at >= r.reported_at;
    IF pending_count + reception_count <> 1
       OR NOT EXISTS (SELECT 1 FROM damage_report_contexts WHERE damage_report_id = report_id)
       OR EXISTS (SELECT 1 FROM damage_report_receptions WHERE damage_report_id = report_id AND received_at < r.reported_at)
    THEN RAISE EXCEPTION 'Invalid damage report state' USING ERRCODE = '23514'; END IF;
    RETURN NULL;
END $fn$;

CREATE CONSTRAINT TRIGGER damage_report_state AFTER INSERT OR UPDATE OR DELETE ON damage_reports
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION damage_report_integrity();
CREATE CONSTRAINT TRIGGER damage_report_pending_state AFTER INSERT OR UPDATE OR DELETE ON damage_report_pending_devices
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION damage_report_integrity();
CREATE CONSTRAINT TRIGGER damage_report_reception_state AFTER INSERT OR UPDATE OR DELETE ON damage_report_receptions
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION damage_report_integrity();
CREATE CONSTRAINT TRIGGER damage_report_context_state AFTER INSERT OR UPDATE OR DELETE ON damage_report_contexts
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION damage_report_integrity();

CREATE FUNCTION damage_report_immutable() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF TG_OP = 'DELETE' OR TG_TABLE_NAME IN ('damage_report_history','damage_report_receptions','damage_report_requests') THEN
        RAISE EXCEPTION 'Damage report records are immutable' USING ERRCODE = '23514';
    END IF;
    IF TG_TABLE_NAME = 'damage_reports' THEN
        IF ROW(NEW.id,NEW.report_code,NEW.reporting_department_id,NEW.reporter_account_id,NEW.reported_at,NEW.create_request_id,NEW.create_request_hash)
           IS DISTINCT FROM ROW(OLD.id,OLD.report_code,OLD.reporting_department_id,OLD.reporter_account_id,OLD.reported_at,OLD.create_request_id,OLD.create_request_hash)
           OR NEW.row_version <> OLD.row_version + 1 THEN
            RAISE EXCEPTION 'Immutable report identity or invalid version' USING ERRCODE = '23514';
        END IF;
        IF NEW.device_id <> OLD.device_id AND
           (EXISTS (SELECT 1 FROM damage_report_receptions WHERE damage_report_id = OLD.id)
            OR EXISTS (SELECT 1 FROM repair_item_details WHERE damage_report_id = OLD.id)) THEN
            RAISE EXCEPTION 'Report device is fixed' USING ERRCODE = '23514';
        END IF;
    ELSIF TG_TABLE_NAME = 'damage_report_contexts' THEN
        IF ROW(NEW.damage_report_id,NEW.reporter_name_snapshot,NEW.reporting_department_name_snapshot)
           IS DISTINCT FROM ROW(OLD.damage_report_id,OLD.reporter_name_snapshot,OLD.reporting_department_name_snapshot) THEN
            RAISE EXCEPTION 'Immutable reporting context' USING ERRCODE = '23514';
        END IF;
    ELSIF TG_TABLE_NAME = 'damage_report_evidence' THEN
        IF OLD.removed_at IS NOT NULL OR NEW.removed_at IS NULL OR
           (to_jsonb(NEW) - 'removed_at' - 'removed_by_account_id') IS DISTINCT FROM (to_jsonb(OLD) - 'removed_at' - 'removed_by_account_id') THEN
            RAISE EXCEPTION 'Immutable evidence metadata' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END $fn$;

CREATE TRIGGER damage_report_immutable BEFORE UPDATE OR DELETE ON damage_reports FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();
CREATE TRIGGER damage_context_immutable BEFORE UPDATE OR DELETE ON damage_report_contexts FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();
CREATE TRIGGER damage_reception_immutable BEFORE UPDATE OR DELETE ON damage_report_receptions FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();
CREATE TRIGGER damage_evidence_immutable BEFORE UPDATE OR DELETE ON damage_report_evidence FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();
CREATE TRIGGER damage_history_immutable BEFORE UPDATE OR DELETE ON damage_report_history FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();
CREATE TRIGGER damage_request_immutable BEFORE UPDATE OR DELETE ON damage_report_requests FOR EACH ROW EXECUTE FUNCTION damage_report_immutable();

CREATE FUNCTION damage_report_repair_link() RETURNS trigger LANGUAGE plpgsql AS $fn$
DECLARE report_device uuid; item_device uuid; kind text;
BEGIN
    IF NEW.damage_report_id IS NULL THEN RETURN NEW; END IF;
    SELECT device_id INTO report_device FROM damage_reports WHERE id = NEW.damage_report_id FOR UPDATE;
    SELECT i.device_id, o.order_type INTO item_device, kind FROM service_order_items i JOIN service_orders o ON o.id = i.service_order_id
        WHERE i.id = NEW.service_order_item_id;
    IF report_device IS NULL OR report_device IS DISTINCT FROM item_device OR kind IS DISTINCT FROM 'Repair' THEN
        RAISE EXCEPTION 'Invalid damage report repair link' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $fn$;
CREATE TRIGGER damage_report_repair_link BEFORE INSERT OR UPDATE ON repair_item_details FOR EACH ROW EXECUTE FUNCTION damage_report_repair_link();

CREATE FUNCTION damage_report_repair_parent() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF TG_TABLE_NAME = 'service_order_items' THEN
        IF EXISTS (SELECT 1 FROM repair_item_details d JOIN damage_reports r ON r.id = d.damage_report_id
            WHERE d.service_order_item_id = NEW.id AND (r.device_id <> NEW.device_id OR
            NOT EXISTS (SELECT 1 FROM service_orders o WHERE o.id = NEW.service_order_id AND o.order_type = 'Repair'))) THEN
            RAISE EXCEPTION 'Invalid repair item device' USING ERRCODE = '23514';
        END IF;
    ELSE
        IF NEW.order_type <> 'Repair' AND EXISTS (SELECT 1 FROM service_order_items i JOIN repair_item_details d
            ON d.service_order_item_id = i.id WHERE i.service_order_id = NEW.id AND d.damage_report_id IS NOT NULL) THEN
            RAISE EXCEPTION 'Invalid linked repair order type' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END $fn$;
CREATE TRIGGER damage_report_repair_item BEFORE UPDATE OF device_id,service_order_id ON service_order_items
    FOR EACH ROW EXECUTE FUNCTION damage_report_repair_parent();
CREATE TRIGGER damage_report_repair_order BEFORE UPDATE OF order_type ON service_orders
    FOR EACH ROW EXECUTE FUNCTION damage_report_repair_parent();

""";
    public const string Down = """
DROP TRIGGER IF EXISTS damage_report_repair_link ON repair_item_details;
DROP TRIGGER IF EXISTS damage_report_repair_item ON service_order_items;
DROP TRIGGER IF EXISTS damage_report_repair_order ON service_orders;
DROP FUNCTION IF EXISTS damage_report_repair_link();
DROP FUNCTION IF EXISTS damage_report_repair_parent();
DROP FUNCTION IF EXISTS damage_report_integrity() CASCADE;
DROP FUNCTION IF EXISTS damage_report_immutable() CASCADE;
""";
}
