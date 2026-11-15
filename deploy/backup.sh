#!/bin/sh
# Writes a compressed PostgreSQL dump of PulseWatch to backups/ and deletes dumps older than 14 days.
# Run it nightly from cron (docs/deployment.md §7):
#   15 3 * * * cd /opt/pulsewatch && ./deploy/backup.sh >> backups/backup.log 2>&1
#
# Settings (environment variables):
#   PW_BACKUP_DIR        where dumps go (default: backups)
#   PW_BACKUP_KEEP_DAYS  how long to keep them (default: 14)
#   PW_COMPOSE           compose command (default: docker compose -f docker-compose.prod.yml)
#
# Copy the dumps off the server too (e.g. rclone or restic to object storage): a backup on the same disk
# does not survive losing the disk.

set -eu
cd "$(dirname "$0")/.."

DIR="${PW_BACKUP_DIR:-backups}"
KEEP_DAYS="${PW_BACKUP_KEEP_DAYS:-14}"
COMPOSE="${PW_COMPOSE:-docker compose -f docker-compose.prod.yml}"

mkdir -p "$DIR"
chmod 700 "$DIR"
umask 077

FILE="$DIR/pulsewatch-$(date -u +%Y%m%dT%H%M%SZ).dump"

# Custom format: compressed, and pg_restore can restore single tables from it.
$COMPOSE exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' > "$FILE.partial"
mv "$FILE.partial" "$FILE"

find "$DIR" -name 'pulsewatch-*.dump' -type f -mtime +"$KEEP_DAYS" -delete

echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) backup written: $FILE ($(du -h "$FILE" | cut -f1))"
