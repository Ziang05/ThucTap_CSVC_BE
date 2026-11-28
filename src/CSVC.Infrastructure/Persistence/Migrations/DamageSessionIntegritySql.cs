namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class DamageSessionIntegritySql
{
    public const string Up = """
        CREATE FUNCTION enforce_damage_report_decision() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM damage_reports r
                JOIN damage_report_receptions reception ON reception.damage_report_id = r.id
                WHERE r.id = NEW.damage_report_id AND r.device_id = NEW.device_id
                    AND r.incident_type::text = 'DAMAGE' AND NEW.decided_at >= reception.received_at
            ) THEN
                RAISE EXCEPTION 'Damage verification requires a received damage report for the same device' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $fn$;
        CREATE CONSTRAINT TRIGGER damage_report_decision_valid AFTER INSERT ON damage_report_decisions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_damage_report_decision();

        CREATE FUNCTION prevent_damage_record_changes() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            RAISE EXCEPTION 'Damage decisions and inspections are immutable' USING ERRCODE = '23514';
        END $fn$;
        CREATE TRIGGER damage_report_decision_immutable BEFORE UPDATE OR DELETE ON damage_report_decisions
            FOR EACH ROW EXECUTE FUNCTION prevent_damage_record_changes();
        CREATE TRIGGER damage_session_inspection_immutable BEFORE UPDATE OR DELETE ON damage_session_inspections
            FOR EACH ROW EXECUTE FUNCTION prevent_damage_record_changes();

        CREATE FUNCTION enforce_verified_report_fields() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF ROW(NEW.device_id,NEW.incident_type,NEW.incident_description,NEW.detected_at)
                    IS DISTINCT FROM ROW(OLD.device_id,OLD.incident_type,OLD.incident_description,OLD.detected_at)
                AND EXISTS (SELECT 1 FROM damage_report_decisions WHERE damage_report_id = OLD.id) THEN
                RAISE EXCEPTION 'Verified damage report fields are fixed' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER damage_report_verified_fields BEFORE UPDATE ON damage_reports
            FOR EACH ROW EXECUTE FUNCTION enforce_verified_report_fields();

        CREATE FUNCTION enforce_damage_repair_link() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF TG_OP = 'UPDATE' AND OLD.damage_session_id IS NOT NULL AND
                NEW.damage_session_id IS DISTINCT FROM OLD.damage_session_id THEN
                RAISE EXCEPTION 'The damage session link is immutable' USING ERRCODE = '23514';
            END IF;
            IF NEW.damage_session_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM service_orders WHERE id = NEW.service_order_id AND order_type = 'Repair'
            ) THEN
                RAISE EXCEPTION 'Only a repair item may reference a damage session' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER service_item_damage_session_valid BEFORE INSERT OR UPDATE OF damage_session_id ON service_order_items
            FOR EACH ROW EXECUTE FUNCTION enforce_damage_repair_link();

        CREATE FUNCTION enforce_damage_session_inspection() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM service_order_items i
                WHERE i.id = NEW.service_order_item_id AND i.damage_session_id = NEW.damage_session_id
                    AND i.execution_status = 'Completed'
            ) THEN
                RAISE EXCEPTION 'Inspection must reference successful work for this session' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER damage_session_inspection_valid BEFORE INSERT ON damage_session_inspections
            FOR EACH ROW EXECUTE FUNCTION enforce_damage_session_inspection();
        """;

    public const string Down = """
        DROP TRIGGER damage_session_inspection_valid ON damage_session_inspections;
        DROP TRIGGER service_item_damage_session_valid ON service_order_items;
        DROP TRIGGER damage_report_verified_fields ON damage_reports;
        DROP TRIGGER damage_session_inspection_immutable ON damage_session_inspections;
        DROP TRIGGER damage_report_decision_immutable ON damage_report_decisions;
        DROP TRIGGER damage_report_decision_valid ON damage_report_decisions;
        DROP FUNCTION enforce_damage_session_inspection();
        DROP FUNCTION enforce_damage_repair_link();
        DROP FUNCTION enforce_verified_report_fields();
        DROP FUNCTION prevent_damage_record_changes();
        DROP FUNCTION enforce_damage_report_decision();
        """;
}
