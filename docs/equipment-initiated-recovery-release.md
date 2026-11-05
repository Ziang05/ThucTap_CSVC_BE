# Equipment-initiated recovery — 2026-11-05

QTTB can issue one actual Recovery transfer. Source staff accept/reject it, confirm dispatch, then QTTB staff confirm receipt. Ownership changes only at receipt. Existing source-initiated recoveries keep their original flow. See [API contract](device-transfers-api.md#equipment-initiated-recovery) and [migration procedure](device-workflows-release.md).

## Verification

- `docker compose --profile tools run --rm tooling test CSVC.sln`: **272 passed, 0 failed, 0 skipped**, including 15 new PostgreSQL tests for recovery orders. The tests use disposable databases separate from the application database.
- New tests cover one aggregate through all steps and expanded children, manager/staff creation, creator edit/cancel, rejection, session actors, unknown payload fields, old route bypass, stale versions, concurrent retry/accept/edit/cancel, competing device reservations, independent source/receiver conditions, retained service holds, each audit failure rollback and old Recovery migration compatibility.
- Frontend `npm run build`, TypeScript and ESLint of all changed files pass. Existing Vite warnings concern dependency annotations and bundle size.
- Playwright (Edge, mocked API) passes new create/edit and lifecycle, eligible/selected panels, search/paging/source reset, saved child details after ownership changes, source rejection, creator cancellation, role gating and mobile receipt. Existing source-initiated Recovery browser regression also passes.
- EF model check reports no pending model changes.

## Local deployment

Built `csvc-api:local`; backed up PostgreSQL and verified the custom archive before applying `20261105032457_EquipmentInitiatedRecovery`. Compared row counts and hashes of the old columns in all 52 application tables with the API stopped: all matched. Restarted the API and checked the new routes in Swagger. No reset, seed or account/device/old-transfer rewrite was performed.

The migration adds columns, constraints, actor references and indexes. Existing initiation defaults to `SourceDepartment`. Downgrade is blocked once EquipmentDepartment records exist; review backup restoration rather than dropping signed workflow history.

The source department sees new orders in its existing scoped transfer list. There is no current notification delivery infrastructure, so this release adds no separate notification transport.
