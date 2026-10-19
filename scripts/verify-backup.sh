#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
if [[ $# != 1 ]]; then echo 'Usage: bash scripts/verify-backup.sh .backups/<backup-directory>' >&2; exit 1; fi
backup_path=$(realpath -- "$1")
[[ -f "$backup_path/READY" && -f "$backup_path/database.dump" ]] || { echo 'Incomplete backup' >&2; exit 1; }
(cd "$backup_path" && sha256sum --check database.sha256)
if [[ -d "$backup_path/attachments" && -f "$backup_path/attachments.sha256" ]]; then
  (cd "$backup_path" && sha256sum --check attachments.sha256)
elif [[ -d "$backup_path/attachments" || -f "$backup_path/attachments.sha256" ]]; then
  echo 'Incomplete attachment backup or checksum manifest' >&2
  exit 1
else
  echo 'Legacy database-only backup: attachment restore is not covered.' >&2
fi
export BACKUP_CHECK_FILE="$backup_path/database.dump"
# Unique Compose project prevents simultaneous verifications from sharing a database.
check_project="omnidesk-backup-check-$(date -u +%s)-$$"
cleanup() { docker compose -p "$check_project" -f compose.backup-check.yaml down; }
trap cleanup EXIT
docker compose -p "$check_project" -f compose.backup-check.yaml up -d --wait
docker compose -p "$check_project" -f compose.backup-check.yaml exec -T db pg_restore --exit-on-error --no-owner --no-privileges -U postgres -d omnidesk_verify /backup/database.dump
docker compose -p "$check_project" -f compose.backup-check.yaml exec -T db psql -v ON_ERROR_STOP=1 -U postgres -d omnidesk_verify -c 'SELECT COUNT(*) AS applied_migrations FROM "__EFMigrationsHistory"; SELECT COUNT(*) AS accounts FROM "AspNetUsers"; SELECT COUNT(*) AS devices FROM "Devices";'
if [[ -d "$backup_path/attachments" ]]; then
  attachment_keys=$(docker compose -p "$check_project" -f compose.backup-check.yaml exec -T db \
    psql -v ON_ERROR_STOP=1 -At -U postgres -d omnidesk_verify -c 'SELECT "StorageKey" FROM "SupportAttachments" ORDER BY "StorageKey";')
  while IFS= read -r key; do
    [[ -z "$key" ]] && continue
    [[ "$key" =~ ^[a-f0-9]{32}$ ]] || { echo 'Invalid attachment storage key in restored database' >&2; exit 1; }
    [[ -f "$backup_path/attachments/$key" ]] || { echo "Missing blob for restored attachment $key" >&2; exit 1; }
  done <<< "$attachment_keys"
  echo 'Every restored attachment row references a checksum-verified blob.'
fi
echo 'Database restore verified in an isolated temporary PostgreSQL instance. Production unchanged.'
