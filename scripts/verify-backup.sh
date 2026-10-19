#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
if [[ $# != 1 ]]; then echo 'Usage: bash scripts/verify-backup.sh .backups/<backup-directory>' >&2; exit 1; fi
backup_path=$(realpath -- "$1")
[[ -f "$backup_path/READY" && -f "$backup_path/database.dump" ]] || { echo 'Incomplete backup' >&2; exit 1; }
(cd "$backup_path" && sha256sum --check database.sha256)
export BACKUP_CHECK_FILE="$backup_path/database.dump"
# Unique Compose project prevents simultaneous verifications from sharing a database.
check_project="omnidesk-backup-check-$(date -u +%s)-$$"
cleanup() { docker compose -p "$check_project" -f compose.backup-check.yaml down; }
trap cleanup EXIT
docker compose -p "$check_project" -f compose.backup-check.yaml up -d --wait
docker compose -p "$check_project" -f compose.backup-check.yaml exec -T db pg_restore --exit-on-error --no-owner --no-privileges -U postgres -d omnidesk_verify /backup/database.dump
docker compose -p "$check_project" -f compose.backup-check.yaml exec -T db psql -v ON_ERROR_STOP=1 -U postgres -d omnidesk_verify -c 'SELECT COUNT(*) AS applied_migrations FROM "__EFMigrationsHistory"; SELECT COUNT(*) AS accounts FROM "AspNetUsers"; SELECT COUNT(*) AS devices FROM "Devices";'
echo 'Database restore verified in an isolated temporary PostgreSQL instance. Production unchanged.'
