#!/usr/bin/env bash
set -Eeuo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
temp=$(mktemp -d /tmp/omnidesk-backup-retention.XXXXXX)
trap 'rm -rf -- "$temp"' EXIT
root="$temp/backups"
mkdir -m 700 "$root"

prepared=$(bash "$repo/scripts/prepare-backup-directory.sh" "$temp/prepared")
[[ "$prepared" == "$temp/prepared" && -d "$prepared" ]]
[[ "$(stat -c '%a' "$prepared")" == 700 ]]
ln -s "$root" "$temp/backups-link"
if bash "$repo/scripts/prepare-backup-directory.sh" "$temp/backups-link" >/dev/null 2>&1; then
  echo 'FAIL: a symbolic link was accepted as the backup directory.' >&2
  exit 1
fi
printf 'not a directory\n' > "$temp/backups-file"
if bash "$repo/scripts/prepare-backup-directory.sh" "$temp/backups-file" >/dev/null 2>&1; then
  echo 'FAIL: a regular file was accepted as the backup directory.' >&2
  exit 1
fi

old="$root/20200101T000000Z.old123"
incomplete="$root/20200101T000000Z.pending123"
current="$root/20990101T000000Z.new123"
mkdir "$old" "$incomplete" "$current"
touch "$old/READY" "$current/READY"
printf 'verified fixture\n' > "$old/database.dump"
printf 'incomplete fixture\n' > "$incomplete/database.dump"

target="$temp/linked-target"
mkdir "$target"
touch "$target/READY"
ln -s "$target" "$root/20200101T000000Z.link123"

disabled=$(bash "$repo/scripts/prune-backups.sh" "$root" 0)
[[ "$disabled" == *'disabled'* ]]
[[ -d "$old" ]]

removed=$(bash "$repo/scripts/prune-backups.sh" "$root" 90)
[[ "$removed" == *'removed 1 verified backup(s)'* ]]
[[ ! -e "$old" ]]
[[ -d "$incomplete" ]]
[[ -d "$current" ]]
[[ -L "$root/20200101T000000Z.link123" && -d "$target" ]]

if bash "$repo/scripts/prune-backups.sh" "$root" -1 >/dev/null 2>&1; then
  echo 'FAIL: negative retention was accepted.' >&2
  exit 1
fi
if bash "$repo/scripts/prune-backups.sh" "$root" 36501 >/dev/null 2>&1; then
  echo 'FAIL: out-of-range retention was accepted.' >&2
  exit 1
fi
if bash "$repo/scripts/prune-backups.sh" "$target/READY" 90 >/dev/null 2>&1; then
  echo 'FAIL: a file was accepted as the backup directory.' >&2
  exit 1
fi
if ! bash "$repo/scripts/prune-backups.sh" --validate-only 90 >/dev/null; then
  echo 'FAIL: valid retention configuration was rejected by the preflight.' >&2
  exit 1
fi

echo 'PASS: backup retention is opt-in, validates early, prunes only old READY direct children, rejects unsafe roots, and preserves incomplete backups and symlinks.'
