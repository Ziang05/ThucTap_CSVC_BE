namespace CSVC.Infrastructure.Persistence.Migrations;

internal static class InventoryIntegritySql
{
    public const string Up = """
-- Actual inspectors are derived from initial checked_by, NOT from correction editor.
CREATE OR REPLACE VIEW inventory_actual_inspectors AS
SELECT
  pr.slip_id,
  r.checked_by_account_id AS account_id,
  count(*)::bigint AS checked_items,
  min(r.checked_at) AS first_checked_at
FROM inventory_results AS r
JOIN inventory_plan_items AS pi ON pi.id = r.plan_item_id
JOIN inventory_plan_revisions AS pr ON pr.id = pi.revision_id
GROUP BY pr.slip_id, r.checked_by_account_id;

-- Progress uses CURRENT effective exists, after all corrections.
CREATE OR REPLACE VIEW inventory_progress AS
SELECT
  s.id AS slip_id,
  count(pi.id)::bigint AS total_items,
  count(r.id)::bigint AS checked_items,
  count(*) FILTER (WHERE r.exists IS TRUE)::bigint AS exists_items,
  count(*) FILTER (WHERE r.exists IS FALSE)::bigint AS not_exists_items,
  (count(pi.id) - count(r.id))::bigint AS unchecked_items
FROM inventory_slips AS s
LEFT JOIN LATERAL (
  SELECT pr.id
  FROM inventory_plan_revisions AS pr
  WHERE pr.slip_id = s.id AND pr.status = 'PUBLISHED'
  ORDER BY pr.revision_no DESC
  LIMIT 1
) AS current_plan ON TRUE
LEFT JOIN inventory_plan_items AS pi ON pi.revision_id = current_plan.id
LEFT JOIN inventory_results AS r ON r.plan_item_id = pi.id
GROUP BY s.id;

-- REQUIRED in service layer (not expressible as ordinary CHECK constraints):
-- 1. Transactionally lock inventory_slips when writing any item result or correction;
--    accept only IN_PROGRESS, only assigned QTTB account for the active revision.
-- 2. UC-KK-05: INSERT result ONLY if plan_item_id unrecorded, UNIQUE guards race;
--    INSERT RESULT_RECORDED into inventory_item_history IN THE SAME TRANSACTION.
-- 3. UX: show old/new result, then an explicit second confirmation dialog.
--    This dialog is NOT persisted, tokenized, or audited as a business event.
-- 4. UC-KK-16 backend: one correction request after UI confirmation.
--    Check JWT actor/assigned QTTB, status IN_PROGRESS, reason, new_exists <> current,
--    expected_version; atomically UPDATE result and INSERT RESULT_CORRECTED history.
--    Reject stale versions. Idempotent retry returns the committed result.
--    Backend does not and cannot infer how many UI clicks were made.
-- 5. On COMPLETED, forbid modifications to result, item, revisions, and slip.
-- 6. On finalization, require total_items > 0 and unchecked_items = 0,
--    lock slip/results while creating final snapshot/report.
-- 7. Published revisions and their items/assignees must be immutable.
-- 8. Departments only see published PREPARING plan/items for their department;
--    they cannot see results, inspector names, item history, or other PBs.
-- 9. Validate plan item/revision belongs to the target slip and only one plan
--    revision receives results per slip. Check role from accounts/profile.
-- 10. Device's status, condition, department, area, and both hold mechanisms
--    MUST NOT be updated from inventory results.

-- Additional defensive database guards. These do NOT replace service checks.
-- All result writes lock the parent slip, serializing against completion.
CREATE OR REPLACE FUNCTION inv_guard_result_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  v_status text;
  v_revision_id uuid;
  v_slip_id uuid;
  v_latest_published_revision_id uuid;
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'INVENTORY_RESULT_DELETE_FORBIDDEN';
  END IF;

  SELECT s.id, s.status, pr.id
    INTO v_slip_id, v_status, v_revision_id
  FROM inventory_plan_items pi
  JOIN inventory_plan_revisions pr ON pr.id = pi.revision_id
  JOIN inventory_slips s ON s.id = pr.slip_id
  WHERE pi.id = NEW.plan_item_id
  FOR UPDATE OF s;

  IF NOT FOUND OR v_status <> 'IN_PROGRESS' THEN
    RAISE EXCEPTION 'INVENTORY_RESULT_REQUIRES_IN_PROGRESS';
  END IF;

  SELECT pr.id INTO v_latest_published_revision_id
  FROM inventory_plan_revisions pr
  WHERE pr.slip_id = v_slip_id AND pr.status = 'PUBLISHED'
  ORDER BY pr.revision_no DESC
  LIMIT 1;

  IF v_latest_published_revision_id IS DISTINCT FROM v_revision_id THEN
    RAISE EXCEPTION 'INVENTORY_ITEM_NOT_IN_ACTIVE_REVISION';
  END IF;

  IF TG_OP = 'INSERT' THEN
    IF NEW.version <> 0
       OR NEW.last_corrected_by_account_id IS NOT NULL
       OR NEW.last_corrected_at IS NOT NULL THEN
      RAISE EXCEPTION 'INVALID_INITIAL_INVENTORY_RESULT';
    END IF;
  ELSE
    IF NEW.id IS DISTINCT FROM OLD.id
       OR NEW.plan_item_id IS DISTINCT FROM OLD.plan_item_id
       OR NEW.checked_by_account_id IS DISTINCT FROM OLD.checked_by_account_id
       OR NEW.checked_at IS DISTINCT FROM OLD.checked_at
       OR NEW.initial_request_id IS DISTINCT FROM OLD.initial_request_id THEN
      RAISE EXCEPTION 'INITIAL_INSPECTOR_AND_ITEM_IMMUTABLE';
    END IF;
    IF NEW.exists IS NOT DISTINCT FROM OLD.exists THEN
      RAISE EXCEPTION 'INVENTORY_VALUE_UNCHANGED';
    END IF;
    IF NEW.version <> OLD.version + 1
       OR NEW.last_corrected_by_account_id IS NULL
       OR NEW.last_corrected_at IS NULL THEN
      RAISE EXCEPTION 'INVALID_INVENTORY_CORRECTION_VERSION';
    END IF;
  END IF;

  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_inv_guard_result_mutation
BEFORE INSERT OR UPDATE OR DELETE ON inventory_results
FOR EACH ROW EXECUTE FUNCTION inv_guard_result_mutation();

-- Prevent reopening or mutating completed slips; also require every item
-- in the latest published plan to have a current result before completing.
CREATE OR REPLACE FUNCTION inv_guard_slip_completion()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  v_rev uuid;
  v_total bigint;
  v_checked bigint;
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'INVENTORY_SLIP_DELETE_FORBIDDEN';
  END IF;

  IF OLD.status = 'COMPLETED' THEN
    RAISE EXCEPTION 'INVENTORY_COMPLETED_IMMUTABLE';
  END IF;

  IF NEW.status = 'COMPLETED' AND OLD.status <> 'COMPLETED' THEN
    IF OLD.status <> 'IN_PROGRESS' THEN
      RAISE EXCEPTION 'INVENTORY_MUST_BE_IN_PROGRESS_TO_COMPLETE';
    END IF;

    SELECT pr.id INTO v_rev
    FROM inventory_plan_revisions pr
    WHERE pr.slip_id = NEW.id AND pr.status = 'PUBLISHED'
    ORDER BY pr.revision_no DESC
    LIMIT 1;

    SELECT count(pi.id), count(r.id) INTO v_total, v_checked
    FROM inventory_plan_items pi
    LEFT JOIN inventory_results r ON r.plan_item_id = pi.id
    WHERE pi.revision_id = v_rev;

    IF v_rev IS NULL OR v_total = 0 OR v_total <> v_checked THEN
      RAISE EXCEPTION 'INVENTORY_HAS_UNCHECKED_ITEMS';
    END IF;
    IF NEW.completed_by_account_id IS NULL OR NEW.completed_at IS NULL THEN
      RAISE EXCEPTION 'INVENTORY_COMPLETION_ACTOR_TIME_REQUIRED';
    END IF;
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_inv_guard_slip_completion
BEFORE UPDATE OR DELETE ON inventory_slips
FOR EACH ROW EXECUTE FUNCTION inv_guard_slip_completion();

-- Published plan revisions and their snapshots must not change silently.
CREATE OR REPLACE FUNCTION inv_guard_revision_immutability()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    RAISE EXCEPTION 'INVENTORY_PLAN_REVISION_DELETE_FORBIDDEN';
  END IF;
  IF OLD.status = 'PUBLISHED' THEN
    RAISE EXCEPTION 'INVENTORY_PUBLISHED_REVISION_IMMUTABLE';
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_inv_guard_revision_immutability
BEFORE UPDATE OR DELETE ON inventory_plan_revisions
FOR EACH ROW EXECUTE FUNCTION inv_guard_revision_immutability();

CREATE OR REPLACE FUNCTION inv_guard_plan_snapshot_immutability()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  v_rev_id uuid;
  v_state text;
BEGIN
  IF TG_OP = 'DELETE' OR TG_OP = 'UPDATE' THEN
    v_rev_id := OLD.revision_id;
  ELSE
    v_rev_id := NEW.revision_id;
  END IF;
  SELECT status INTO v_state FROM inventory_plan_revisions WHERE id = v_rev_id;
  IF v_state = 'PUBLISHED' THEN
    RAISE EXCEPTION 'INVENTORY_PUBLISHED_PLAN_SNAPSHOT_IMMUTABLE';
  END IF;
  -- Also prevent moving a draft item into a published revision.
  IF TG_OP = 'UPDATE' AND NEW.revision_id IS DISTINCT FROM OLD.revision_id THEN
    SELECT status INTO v_state FROM inventory_plan_revisions WHERE id = NEW.revision_id;
    IF v_state = 'PUBLISHED' THEN
      RAISE EXCEPTION 'INVENTORY_PUBLISHED_PLAN_SNAPSHOT_IMMUTABLE';
    END IF;
  END IF;
  IF TG_OP = 'DELETE' THEN
    RETURN OLD;
  END IF;
  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_inv_plan_items_immutable
BEFORE INSERT OR UPDATE OR DELETE ON inventory_plan_items
FOR EACH ROW EXECUTE FUNCTION inv_guard_plan_snapshot_immutability();

CREATE TRIGGER trg_inv_assignees_immutable
BEFORE INSERT OR UPDATE OR DELETE ON inventory_revision_assignees
FOR EACH ROW EXECUTE FUNCTION inv_guard_plan_snapshot_immutability();

-- Item history and slip audit trails are append-only.
CREATE OR REPLACE FUNCTION inv_reject_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  RAISE EXCEPTION 'INVENTORY_AUDIT_APPEND_ONLY';
END;
$$;

CREATE TRIGGER trg_inv_item_history_append_only
BEFORE UPDATE OR DELETE ON inventory_item_history
FOR EACH ROW EXECUTE FUNCTION inv_reject_audit_mutation();

CREATE TRIGGER trg_inv_history_append_only
BEFORE UPDATE OR DELETE ON inventory_history
FOR EACH ROW EXECUTE FUNCTION inv_reject_audit_mutation();

CREATE TRIGGER trg_inv_policy_history_append_only
BEFORE UPDATE OR DELETE ON inventory_notice_policy_history
FOR EACH ROW EXECUTE FUNCTION inv_reject_audit_mutation();

-- Validate BOTH types of history event. This trigger runs during the
-- transaction, before its commit; the service must update the current result
-- and insert item history atomically. Slip-level history does NOT copy events.
CREATE OR REPLACE FUNCTION inv_guard_item_history_insert()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
  v_state text;
  v_exists boolean;
  v_version integer;
  v_original_actor uuid;
  v_original_at timestamptz;
  v_initial_request uuid;
  v_last_editor uuid;
  v_last_corrected_at timestamptz;
  v_previous_exists boolean;
BEGIN
  SELECT s.status, r.exists, r.version, r.checked_by_account_id,
         r.checked_at, r.initial_request_id, r.last_corrected_by_account_id,
         r.last_corrected_at
    INTO v_state, v_exists, v_version, v_original_actor,
         v_original_at, v_initial_request, v_last_editor,
         v_last_corrected_at
  FROM inventory_results AS r
  JOIN inventory_plan_items AS pi ON pi.id = r.plan_item_id
  JOIN inventory_plan_revisions AS pr ON pr.id = pi.revision_id
  JOIN inventory_slips AS s ON s.id = pr.slip_id
  WHERE r.id = NEW.result_id
  FOR UPDATE OF s;

  IF NOT FOUND OR v_state <> 'IN_PROGRESS' THEN
    RAISE EXCEPTION 'INVENTORY_ITEM_HISTORY_REQUIRES_IN_PROGRESS';
  END IF;

  -- At insertion time the result must match the new version and value.
  IF v_exists IS DISTINCT FROM NEW.after_exists
     OR v_version IS DISTINCT FROM NEW.version_after THEN
    RAISE EXCEPTION 'ITEM_HISTORY_NOT_MATCHING_CURRENT_RESULT';
  END IF;

  IF NEW.action_code = 'RESULT_RECORDED' THEN
    IF NEW.actor_account_id IS DISTINCT FROM v_original_actor
       OR NEW.occurred_at IS DISTINCT FROM v_original_at
       OR NEW.request_id IS DISTINCT FROM v_initial_request THEN
      RAISE EXCEPTION 'INITIAL_ITEM_HISTORY_ACTOR_OR_REQUEST_MISMATCH';
    END IF;
  ELSIF NEW.action_code = 'RESULT_CORRECTED' THEN
    IF v_last_editor IS DISTINCT FROM NEW.actor_account_id
       OR v_last_corrected_at IS DISTINCT FROM NEW.occurred_at THEN
      RAISE EXCEPTION 'ITEM_HISTORY_EDITOR_MISMATCH';
    END IF;

    -- Check the BEFORE value against the most recent committed item event.
    -- The UI's second confirmation is intentionally NOT represented here.
    SELECT h.after_exists INTO v_previous_exists
    FROM inventory_item_history AS h
    WHERE h.result_id = NEW.result_id
      AND h.version_after = NEW.version_before;
    IF NOT FOUND OR v_previous_exists IS DISTINCT FROM NEW.before_exists THEN
      RAISE EXCEPTION 'ITEM_HISTORY_PREVIOUS_VALUE_MISMATCH';
    END IF;
  ELSE
    RAISE EXCEPTION 'UNKNOWN_INVENTORY_ITEM_HISTORY_ACTION';
  END IF;

  RETURN NEW;
END;
$$;

CREATE TRIGGER trg_inv_guard_item_history_insert
BEFORE INSERT ON inventory_item_history
FOR EACH ROW EXECUTE FUNCTION inv_guard_item_history_insert();

-- Additional safety: every INSERT or UPDATE of the CURRENT result MUST have
-- a corresponding append-only item history event by the end of the same DB
-- transaction. Unlike an ordinary BEFORE trigger, a deferred constraint
-- trigger can see the history inserted later in the same transaction.
CREATE OR REPLACE FUNCTION inv_require_item_history_on_result_change()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM inventory_item_history h
    WHERE h.result_id = NEW.id
      AND h.version_after = NEW.version
      AND h.after_exists = NEW.exists
      AND h.action_code = CASE WHEN TG_OP = 'INSERT'
        THEN 'RESULT_RECORDED'::text
        ELSE 'RESULT_CORRECTED'::text END
      AND h.actor_account_id = CASE WHEN TG_OP = 'INSERT'
        THEN NEW.checked_by_account_id ELSE NEW.last_corrected_by_account_id END
  ) THEN
    RAISE EXCEPTION 'ITEM_RESULT_CHANGE_WITHOUT_HISTORY';
  END IF;
  RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_inv_require_item_history
AFTER INSERT OR UPDATE ON inventory_results
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION inv_require_item_history_on_result_change();


-- Integration additions: existing account/profile/role/settings tables remain authoritative.
CREATE OR REPLACE FUNCTION inv_guard_revision_immutability() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_state text;
BEGIN
  IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'INVENTORY_PLAN_REVISION_DELETE_FORBIDDEN'; END IF;
  SELECT status INTO v_state FROM inventory_slips WHERE id=NEW.slip_id FOR UPDATE;
  IF v_state NOT IN ('PENDING_APPROVAL','PREPARING') THEN RAISE EXCEPTION 'INVENTORY_PLAN_LOCKED'; END IF;
  IF TG_OP = 'INSERT' THEN
    IF NEW.status <> 'DRAFT' THEN RAISE EXCEPTION 'INVENTORY_REVISION_MUST_START_DRAFT'; END IF;
  ELSE
    IF OLD.status <> 'DRAFT' OR NEW.id <> OLD.id OR NEW.slip_id <> OLD.slip_id OR NEW.revision_no <> OLD.revision_no
      THEN RAISE EXCEPTION 'INVENTORY_PUBLISHED_REVISION_IMMUTABLE'; END IF;
    IF NEW.status = 'PUBLISHED' AND (NOT EXISTS (SELECT 1 FROM inventory_plan_items WHERE revision_id=NEW.id)
        OR NOT EXISTS (SELECT 1 FROM inventory_revision_assignees WHERE revision_id=NEW.id))
      THEN RAISE EXCEPTION 'INVENTORY_EMPTY_PUBLICATION'; END IF;
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER trg_inv_guard_revision_immutability ON inventory_plan_revisions;
CREATE TRIGGER trg_inv_guard_revision_immutability BEFORE INSERT OR UPDATE OR DELETE ON inventory_plan_revisions
FOR EACH ROW EXECUTE FUNCTION inv_guard_revision_immutability();

CREATE OR REPLACE FUNCTION inv_guard_plan_snapshot_immutability() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_revision uuid; v_state text; v_slip text;
BEGIN
  v_revision := CASE WHEN TG_OP='DELETE' THEN OLD.revision_id ELSE NEW.revision_id END;
  IF TG_OP='UPDATE' AND NEW.revision_id<>OLD.revision_id THEN RAISE EXCEPTION 'INVENTORY_ITEM_REVISION_IMMUTABLE'; END IF;
  SELECT r.status,s.status INTO v_state,v_slip FROM inventory_plan_revisions r
    JOIN inventory_slips s ON s.id=r.slip_id WHERE r.id=v_revision FOR UPDATE OF s;
  IF v_state <> 'DRAFT' OR v_slip NOT IN ('PENDING_APPROVAL','PREPARING') THEN
    RAISE EXCEPTION 'INVENTORY_PUBLISHED_PLAN_SNAPSHOT_IMMUTABLE'; END IF;
  IF TG_OP='DELETE' THEN RETURN OLD; END IF;
  RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION inv_check_result_assignment() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_actor uuid;
BEGIN
  v_actor := CASE WHEN TG_OP='INSERT' THEN NEW.checked_by_account_id ELSE NEW.last_corrected_by_account_id END;
  IF NOT EXISTS (SELECT 1 FROM inventory_plan_items i
    JOIN inventory_revision_assignees ass ON ass.revision_id=i.revision_id
    JOIN accounts a ON a.id=ass.account_id JOIN roles r ON r.id=a.role_id
    JOIN user_profiles p ON p.id=a.user_profile_id JOIN departments d ON d.id=p.department_id
    JOIN equipment_settings cfg ON cfg.id=1 AND cfg.equipment_department_id=p.department_id
    WHERE i.id=NEW.plan_item_id AND ass.account_id=v_actor AND NOT a.is_lock
      AND r.code IN ('QTTB_MANAGER','QTTB_STAFF') AND d.status='ACTIVE') THEN
    RAISE EXCEPTION 'INVENTORY_ASSIGNED_QTTB_REQUIRED'; END IF;
  IF TG_OP='UPDATE' AND (NEW.checker_code_snapshot<>OLD.checker_code_snapshot OR NEW.checker_name_snapshot<>OLD.checker_name_snapshot)
    THEN RAISE EXCEPTION 'INVENTORY_INITIAL_INSPECTOR_IMMUTABLE'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_inv_result_assignment BEFORE INSERT OR UPDATE ON inventory_results
FOR EACH ROW EXECUTE FUNCTION inv_check_result_assignment();

CREATE OR REPLACE FUNCTION inv_require_initial_slip() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.status <> 'PENDING_APPROVAL' OR NEW.version<>0 OR NEW.started_at IS NOT NULL OR NEW.completed_at IS NOT NULL
    THEN RAISE EXCEPTION 'INVENTORY_INVALID_INITIAL_SLIP'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_inv_initial_slip BEFORE INSERT ON inventory_slips FOR EACH ROW EXECUTE FUNCTION inv_require_initial_slip();

CREATE OR REPLACE FUNCTION inv_guard_slip_transition() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_rev inventory_plan_revisions;
BEGIN
  IF NEW.id<>OLD.id OR NEW.display_code<>OLD.display_code OR NEW.create_request_id<>OLD.create_request_id
    OR NEW.created_by_account_id<>OLD.created_by_account_id OR NEW.created_at<>OLD.created_at
    OR NEW.creator_code_snapshot<>OLD.creator_code_snapshot OR NEW.creator_name_snapshot<>OLD.creator_name_snapshot
    THEN RAISE EXCEPTION 'INVENTORY_SLIP_IDENTITY_IMMUTABLE'; END IF;
  IF NEW.version<>OLD.version+1 THEN RAISE EXCEPTION 'INVENTORY_INVALID_SLIP_VERSION'; END IF;
  IF OLD.status <> 'PENDING_APPROVAL' AND
    (NEW.department_id<>OLD.department_id OR NEW.department_code_snapshot<>OLD.department_code_snapshot
      OR NEW.department_name_snapshot<>OLD.department_name_snapshot OR NEW.notes IS DISTINCT FROM OLD.notes)
    THEN RAISE EXCEPTION 'INVENTORY_PUBLISHED_HEADER_IMMUTABLE'; END IF;
  IF NOT ((OLD.status='PENDING_APPROVAL' AND NEW.status IN ('PENDING_APPROVAL','PREPARING','CANCELED'))
    OR (OLD.status='PREPARING' AND NEW.status IN ('PREPARING','IN_PROGRESS','CANCELED'))
    OR (OLD.status='IN_PROGRESS' AND NEW.status='COMPLETED')) THEN
    RAISE EXCEPTION 'INVENTORY_INVALID_TRANSITION'; END IF;
  IF NEW.status IN ('PREPARING','IN_PROGRESS','COMPLETED') THEN
    SELECT * INTO v_rev FROM inventory_plan_revisions WHERE slip_id=NEW.id AND status='PUBLISHED' ORDER BY revision_no DESC LIMIT 1;
    IF NOT FOUND THEN RAISE EXCEPTION 'INVENTORY_NO_PUBLISHED_REVISION'; END IF;
  END IF;
  IF NEW.status='IN_PROGRESS' THEN
    IF NEW.started_at IS NULL OR NEW.started_by_account_id IS NULL OR NEW.started_at < v_rev.planned_start_at
      OR NEW.started_at < v_rev.published_at + v_rev.notice_days_snapshot * interval '24 hours'
      OR EXISTS (SELECT 1 FROM inventory_plan_revisions WHERE slip_id=NEW.id AND status='DRAFT')
      OR NOT EXISTS (SELECT 1 FROM inventory_revision_assignees WHERE revision_id=v_rev.id AND account_id=NEW.started_by_account_id)
      THEN RAISE EXCEPTION 'INVENTORY_START_NOT_ALLOWED'; END IF;
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_inv_slip_transition BEFORE UPDATE ON inventory_slips FOR EACH ROW EXECUTE FUNCTION inv_guard_slip_transition();

-- The active published revision is the only baseline for both views.
CREATE OR REPLACE VIEW inventory_actual_inspectors AS
SELECT pr.slip_id,r.checked_by_account_id AS account_id,count(*)::bigint AS checked_items,min(r.checked_at) AS first_checked_at
FROM inventory_results r JOIN inventory_plan_items i ON i.id=r.plan_item_id
JOIN inventory_plan_revisions pr ON pr.id=i.revision_id
WHERE pr.status='PUBLISHED' AND pr.revision_no=(SELECT max(p.revision_no) FROM inventory_plan_revisions p WHERE p.slip_id=pr.slip_id AND p.status='PUBLISHED')
GROUP BY pr.slip_id,r.checked_by_account_id;

-- Require publication and its in-app delivery to commit together.
CREATE OR REPLACE FUNCTION inv_require_publication_notice() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.status='PUBLISHED' AND NOT EXISTS (SELECT 1 FROM inventory_notifications
    WHERE revision_id=NEW.id AND event_type IN ('PLAN_PUBLISHED','PLAN_UPDATED')) THEN
    RAISE EXCEPTION 'INVENTORY_PUBLICATION_WITHOUT_NOTICE'; END IF;
  RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER trg_inv_publication_notice AFTER INSERT OR UPDATE ON inventory_plan_revisions
DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION inv_require_publication_notice();

CREATE OR REPLACE FUNCTION inv_guard_history_insert() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_slip inventory_slips;
BEGIN
  SELECT * INTO v_slip FROM inventory_slips WHERE id=NEW.slip_id FOR UPDATE;
  IF v_slip.status='COMPLETED' AND (NEW.action_code<>'COMPLETE'
      OR NEW.actor_account_id IS DISTINCT FROM v_slip.completed_by_account_id
      OR EXISTS (SELECT 1 FROM inventory_history WHERE slip_id=NEW.slip_id AND action_code='COMPLETE'))
    THEN RAISE EXCEPTION 'INVENTORY_COMPLETED_HISTORY_IMMUTABLE'; END IF;
  IF v_slip.status='CANCELED' AND (NEW.action_code<>'CANCEL'
      OR NEW.actor_account_id IS DISTINCT FROM v_slip.canceled_by_account_id
      OR EXISTS (SELECT 1 FROM inventory_history WHERE slip_id=NEW.slip_id AND action_code='CANCEL'))
    THEN RAISE EXCEPTION 'INVENTORY_CANCELED_HISTORY_IMMUTABLE'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_inv_history_insert BEFORE INSERT ON inventory_history FOR EACH ROW EXECUTE FUNCTION inv_guard_history_insert();

CREATE OR REPLACE FUNCTION inv_guard_notification_context() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM inventory_plan_revisions WHERE id=NEW.revision_id AND slip_id=NEW.slip_id AND status='PUBLISHED')
    THEN RAISE EXCEPTION 'INVENTORY_NOTIFICATION_CONTEXT_INVALID'; END IF;
  IF TG_OP='UPDATE' AND (NEW.id<>OLD.id OR NEW.event_key<>OLD.event_key OR NEW.slip_id<>OLD.slip_id
      OR NEW.revision_id<>OLD.revision_id OR NEW.recipient_account_id<>OLD.recipient_account_id
      OR NEW.event_type<>OLD.event_type OR NEW.payload_snapshot<>OLD.payload_snapshot OR NEW.published_at<>OLD.published_at)
    THEN RAISE EXCEPTION 'INVENTORY_NOTIFICATION_PAYLOAD_IMMUTABLE'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER trg_inv_notification_context BEFORE INSERT OR UPDATE ON inventory_notifications
FOR EACH ROW EXECUTE FUNCTION inv_guard_notification_context();

""";
    public const string Down = """
DROP VIEW IF EXISTS inventory_actual_inspectors, inventory_progress;
DROP FUNCTION IF EXISTS inv_guard_history_insert() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_notification_context() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_result_mutation() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_slip_completion() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_revision_immutability() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_plan_snapshot_immutability() CASCADE;
DROP FUNCTION IF EXISTS inv_reject_audit_mutation() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_item_history_insert() CASCADE;
DROP FUNCTION IF EXISTS inv_require_item_history_on_result_change() CASCADE;
DROP FUNCTION IF EXISTS inv_check_result_assignment() CASCADE;
DROP FUNCTION IF EXISTS inv_require_initial_slip() CASCADE;
DROP FUNCTION IF EXISTS inv_guard_slip_transition() CASCADE;
DROP FUNCTION IF EXISTS inv_require_publication_notice() CASCADE;
""";
}
