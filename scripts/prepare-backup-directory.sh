#!/usr/bin/env bash
set -Eeuo pipefail

requested=${1:-.backups}
parent_input=$(dirname -- "$requested")
leaf=$(basename -- "$requested")
if [[ -z "$leaf" || "$leaf" == . || "$leaf" == .. ]]; then
  echo 'Backup directory must name a dedicated directory.' >&2
  exit 2
fi
mkdir -p -- "$parent_input"
parent=$(cd -- "$parent_input" && pwd -P)
backup_root="$parent/$leaf"
if [[ -L "$backup_root" || ( -e "$backup_root" && ! -d "$backup_root" ) ]]; then
  echo 'Backup directory must be a real directory, not a file or symbolic link.' >&2
  exit 2
fi
mkdir -m 700 -- "$backup_root" 2>/dev/null || [[ -d "$backup_root" && ! -L "$backup_root" ]]
resolved=$(realpath -e -- "$backup_root")
[[ "$resolved" == "$backup_root" && ! -L "$resolved" && -d "$resolved" ]] || {
  echo 'Backup directory changed while it was being prepared.' >&2
  exit 2
}
chmod 700 -- "$resolved"
printf '%s\n' "$resolved"
