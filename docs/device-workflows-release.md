# Device workflows release guide

This guide covers the schema and API changes for devices, receipts, loans, transfers, and their shared history. The API does **not** apply migrations during normal startup. `--migrate` is an explicit one-off deployment command.

## Before release

1. Review the migration diff and confirm the target environment is backed up. The current development migration is `20261029120000_InventoryFoundation`; preserve migrations already applied in shared or production databases.
2. Build and tag the API image with the release version. Avoid deploying a mutable `latest` tag.
3. Put the API into maintenance mode or stop API instances while the migration runs. Do not run multiple migration jobs concurrently.
4. Take and verify a PostgreSQL backup before changing the schema.

Example backup using the Compose PostgreSQL container:

```powershell
docker compose exec postgres sh -c 'pg_dump -Fc -U "$POSTGRES_USER" "$POSTGRES_DB" -f /tmp/csvc-pre-release.dump'
docker compose exec postgres pg_restore -l /tmp/csvc-pre-release.dump
docker compose cp postgres:/tmp/csvc-pre-release.dump ./csvc-pre-release.dump
```

Keep the backup outside the repository and restrict access because it contains application data. Confirm the copied file exists and has a non-zero size before proceeding.

## Apply and verify

```powershell
docker compose build api
docker compose run --rm api --migrate
docker compose up -d api
docker compose ps
docker compose logs --since 5m api
```

The migration job exits after EF Core applies pending migrations. If it fails, do not start the new API image; inspect the migration error and restore or repair the database before retrying. Check the application health endpoint and Swagger in a Development environment, then verify device list/barcode lookup, receipt completion, loan return, transfer completion, and device history with authorized test accounts.

## Roll back

- **Application-only rollback:** stop the new API and deploy the previously tagged image when the migration is additive and that version is compatible with the expanded schema.
- **Database rollback:** stop all API writers first. Restore the pre-release backup into a clean/recovered database, verify it, then point the previous API release at that database. Restore the whole backup rather than deleting inventory tables by hand; devices and audit history are linked by restrictive foreign keys.
- Do not run `Down` against a shared/production database as an ad hoc rollback. Review the migration's `Down` behavior and use a separately reviewed recovery procedure. The API image exposes only the forward `--migrate` operation.

## Operational notes

- Never commit `.env`, backups, connection strings, or credentials.
- Keep PostgreSQL backup retention and restore access separate from the application container.
- Application logs include request ID and exception type for unhandled failures; they do not serialize request bodies or audit snapshots.
- EF parameter-sensitive logging is not enabled. Do not enable it in a shared or production environment without a reviewed data-redaction policy.
- This guide is not evidence of a PostgreSQL integration run. Execute migration, restore, query-plan, and concurrency checks in a disposable PostgreSQL environment before production release.
