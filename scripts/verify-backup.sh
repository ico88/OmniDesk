#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
staging=false
if [[ "${1:-}" == --staging ]]; then staging=true; shift; fi
if [[ $# != 1 ]]; then echo 'Usage: bash scripts/verify-backup.sh [--staging] .backups/<backup-directory>' >&2; exit 1; fi
backup_path=$(realpath -- "$1")
[[ -f "$backup_path/database.dump" ]] || { echo 'Incomplete backup: database dump missing' >&2; exit 1; }
if [[ "$staging" != true && ! -f "$backup_path/READY" ]]; then
  echo 'Incomplete backup: READY marker missing; staged archives require full verification first.' >&2
  exit 1
fi
if [[ -f "$backup_path/files.sha256" ]]; then
  (cd "$backup_path" && sha256sum --check --quiet files.sha256)
  echo 'Full-file checksum manifest verified.'
else
  echo 'Legacy backup: full-file checksum manifest unavailable.' >&2
fi
(cd "$backup_path" && sha256sum --check database.sha256)
if [[ -d "$backup_path/attachments" && -f "$backup_path/attachments.sha256" ]]; then
  if [[ -s "$backup_path/attachments.sha256" ]]; then
    (cd "$backup_path" && sha256sum --check attachments.sha256)
  else
    echo 'Attachment checksum manifest is empty; database references will still be checked.'
  fi
elif [[ -d "$backup_path/attachments" || -f "$backup_path/attachments.sha256" ]]; then
  echo 'Incomplete attachment backup or checksum manifest' >&2
  exit 1
else
  echo 'Attachment backup missing: blob restore is not covered.' >&2
fi
# Unique Compose project prevents simultaneous verifications from sharing a database.
check_project="omnidesk-backup-check-$(date -u +%s)-$$"
restore_project="omnidesk-restore-check-$(date -u +%s)-$$"
restore_dir=''
cleanup() {
  docker compose -p "$restore_project" -f compose.restore-check.yaml down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker compose -p "$check_project" -f compose.backup-check.yaml down >/dev/null 2>&1 || true
  [[ -z "$restore_dir" ]] || rm -rf -- "$restore_dir"
}
trap cleanup EXIT
export BACKUP_CHECK_FILE="$backup_path/database.dump"
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

if [[ -d "$backup_path/identity_keys" && -d "$backup_path/caddy_data" && -f "$backup_path/db_password" ]]; then
  restore_dir=$(mktemp -d)
  chmod 700 "$restore_dir"
  # Compose file-backed secrets are bind-mounted; the API runs as UID 1654.
  # The containing directory stays private (0700), while the mounted file is read-only.
  install -m 444 -- "$backup_path/db_password" "$restore_dir/db_password"
  export RESTORE_BACKUP_PATH="$backup_path"
  export RESTORE_CHECK_DIR="$restore_dir"
  docker compose -p "$restore_project" -f compose.restore-check.yaml config --quiet
  docker compose -p "$restore_project" -f compose.restore-check.yaml up -d --wait db
  docker compose -p "$restore_project" -f compose.restore-check.yaml run --pull missing --rm --no-deps restore-seed
  docker compose -p "$restore_project" -f compose.restore-check.yaml exec -T db sh -ec \
    'PGPASSWORD="$(cat /run/secrets/db_password)" pg_restore --exit-on-error --no-owner --no-privileges -U omnidesk -d omnidesk /backup/database.dump'
  docker compose -p "$restore_project" -f compose.restore-check.yaml run --rm migrate
  docker compose -p "$restore_project" -f compose.restore-check.yaml up -d --wait api caddy
  if ! docker compose -p "$restore_project" -f compose.restore-check.yaml run --rm probe; then
    # Inspect logs only in memory and report exception class names, never messages or values.
    api_logs=$(docker compose -p "$restore_project" -f compose.restore-check.yaml logs --no-color api 2>/dev/null || true)
    exception_types=$(printf '%s' "$api_logs" | grep -Eo '(System\.)?[[:alnum:].]+Exception' | sort -u | tr '\n' ' ')
    [[ -z "$exception_types" ]] || printf 'Sanitized API diagnostic exception types: %s\n' "$exception_types" >&2
    exit 1
  fi
  docker compose -p "$restore_project" -f compose.restore-check.yaml exec -T db \
    psql -v ON_ERROR_STOP=1 -U omnidesk -d omnidesk \
    -c 'SELECT COUNT(*) AS applied_migrations FROM "__EFMigrationsHistory"; SELECT COUNT(*) AS accounts FROM "AspNetUsers"; SELECT COUNT(*) AS devices FROM "Devices";' >/dev/null
  echo 'Full restore verified: database, migrations, Identity keys, HTTPS API and restored Caddy CA. Production unchanged.'
else
  echo 'Full application restore skipped: this backup lacks identity_keys, caddy_data, or db_password.' >&2
fi
