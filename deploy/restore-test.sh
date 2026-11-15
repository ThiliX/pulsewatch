#!/bin/sh
# Proves a backup can be restored: loads a dump into a scratch database next to the real one, counts the
# rows in the main tables, then drops the scratch database. The live database is never touched.
#
#   ./deploy/restore-test.sh                          # newest dump in backups/
#   ./deploy/restore-test.sh backups/pulsewatch-....dump
#
# Run it after setting up backups and then monthly. For a real restore, see docs/deployment.md §7.

set -eu
cd "$(dirname "$0")/.."

COMPOSE="${PW_COMPOSE:-docker compose -f docker-compose.prod.yml}"
FILE="${1:-$(ls -1t "${PW_BACKUP_DIR:-backups}"/pulsewatch-*.dump 2>/dev/null | head -n 1)}"
SCRATCH=pulsewatch_restore_test

if [ -z "$FILE" ] || [ ! -f "$FILE" ]; then
  echo "No dump found. Run ./deploy/backup.sh first or pass a file." >&2
  exit 1
fi

echo "Restoring $FILE into scratch database $SCRATCH ..."
$COMPOSE exec -T db sh -c "dropdb -U \"\$POSTGRES_USER\" --if-exists $SCRATCH && createdb -U \"\$POSTGRES_USER\" $SCRATCH"
$COMPOSE exec -T db sh -c "pg_restore -U \"\$POSTGRES_USER\" -d $SCRATCH --no-owner --exit-on-error" < "$FILE"

$COMPOSE exec -T db sh -c "psql -U \"\$POSTGRES_USER\" -d $SCRATCH -v ON_ERROR_STOP=1 -c \"
  select 'users' as table_name, count(*) as rows from users
  union all select 'monitors', count(*) from monitors
  union all select 'monitor_checks', count(*) from monitor_checks
  union all select 'incidents', count(*) from incidents
  union all select 'migrations', count(*) from __ef_migrations_history;\""

$COMPOSE exec -T db sh -c "dropdb -U \"\$POSTGRES_USER\" $SCRATCH"
echo "Restore test passed. Scratch database removed."
