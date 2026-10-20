#!/usr/bin/env bash
set -Eeuo pipefail

mode=prune
if [[ "${1:-}" == --validate-only ]]; then
  mode=validate
  backup_root=''
  retention_days=${2:-${OMNIDESK_BACKUP_RETENTION_DAYS:-0}}
else
  backup_root=${1:-.backups}
  retention_days=${2:-${OMNIDESK_BACKUP_RETENTION_DAYS:-0}}
fi

if [[ ! "$retention_days" =~ ^[0-9]{1,5}$ ]] || (( 10#$retention_days > 36500 )); then
  echo 'OMNIDESK_BACKUP_RETENTION_DAYS must be an integer between 0 and 36500.' >&2
  exit 2
fi
retention_days=$((10#$retention_days))
if [[ "$mode" == validate ]]; then
  echo "Backup retention configuration is valid (${retention_days} day(s))."
  exit 0
fi
if (( retention_days == 0 )); then
  echo 'Backup retention is disabled (0 days); no backups pruned.'
  exit 0
fi

if [[ -L "$backup_root" || ! -d "$backup_root" ]]; then
  echo 'Backup directory must exist and must not be a symbolic link.' >&2
  exit 2
fi
backup_root=$(realpath -e -- "$backup_root")
now=$(date -u +%s)
cutoff=$((now - retention_days * 86400))
removed=0

while IFS= read -r -d '' candidate; do
  name=${candidate##*/}
  [[ "$name" =~ ^(20[0-9]{6}T[0-9]{6}Z)\.[A-Za-z0-9]+$ ]] || continue
  [[ ! -L "$candidate" && -d "$candidate" ]] || continue
  [[ ! -L "$candidate/READY" && -f "$candidate/READY" ]] || continue

  timestamp=${BASH_REMATCH[1]}
  backup_epoch=$(date -u -d "${timestamp:0:4}-${timestamp:4:2}-${timestamp:6:2} ${timestamp:9:2}:${timestamp:11:2}:${timestamp:13:2} UTC" +%s 2>/dev/null) || continue
  (( backup_epoch < cutoff )) || continue

  # Canonicalize the direct child again immediately before removal. This also
  # guards against unexpected path components and symlink substitution.
  resolved=$(realpath -e -- "$candidate") || continue
  [[ "$(dirname -- "$resolved")" == "$backup_root" && "$resolved" == "$backup_root/"* ]] || continue
  [[ ! -L "$resolved" && -d "$resolved" && ! -L "$resolved/READY" && -f "$resolved/READY" ]] || continue
  rm -rf --one-file-system -- "$resolved"
  removed=$((removed + 1))
done < <(find "$backup_root" -mindepth 1 -maxdepth 1 -type d -print0)

printf 'Backup retention removed %s verified backup(s) older than %s day(s).\n' "$removed" "$retention_days"
