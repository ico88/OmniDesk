#!/usr/bin/env bash
set -Eeuo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/project/scripts" "$tmp/bin" "$tmp/nas/share"
cp "$repo/scripts/backup-nas.sh" "$tmp/project/scripts/"
cp "$repo/scripts/prepare-backup-directory.sh" "$tmp/project/scripts/"
cp "$repo/scripts/prune-backups.sh" "$tmp/project/scripts/"
cat > "$tmp/bin/bash" <<'STUB'
#!/bin/bash
if [[ "${1:-}" == scripts/backup.sh ]]; then
  mkdir -p "$PWD/.backups/${TEST_BACKUP_NAME}"
  : > "$PWD/.backups/${TEST_BACKUP_NAME}/READY"
  exit 0
fi
if [[ "${1:-}" == scripts/verify-backup.sh ]]; then exit 0; fi
exec /bin/bash "$@"
STUB
cat > "$tmp/bin/docker" <<'STUB'
#!/bin/bash
case " $* " in
  *" --backup-nas-export "*)
    printf '{"settings":{"enabled":true,"server":"nas.example.test","share":"share","username":"backup","password":"test-password","domain":"WORKGROUP","ageRecipient":"%s","scheduleTime":"00:00","timeZoneId":"UTC","keepCount":7},"lastSuccessAt":"%s"}\n' "$TEST_RECIPIENT" "${TEST_LAST_SUCCESS_AT:-}"
    ;;
  *" --backup-nas-record-start "*) echo start >> "$TEST_LOG" ;;
  *" --backup-nas-record-success "*) echo success >> "$TEST_LOG" ;;
  *" --backup-nas-record-failure "*) echo failure >> "$TEST_LOG" ;;
  *) exit 2 ;;
esac
STUB
cat > "$tmp/bin/age" <<'STUB'
#!/bin/bash
[[ "${FAIL_AGE:-0}" != 1 ]] || exit 1
[[ "$1" == -r && "$2" == "$TEST_RECIPIENT" && "$3" == -o ]] || exit 2
cat > "$4"
[[ -s "$4" ]]
STUB
cat > "$tmp/bin/smbclient" <<'STUB'
#!/bin/bash
[[ "${FAIL_SMB:-0}" != 1 ]] || exit 1
command=${*: -1}
share_dir="$TEST_NAS/share"
if [[ "$command" == cd\ *\;* ]]; then
  subdir=${command#cd }; subdir=${subdir%%;*}; share_dir="$share_dir/$subdir"; command=${command#*; }
fi
mkdir -p "$share_dir"
read -r op arg1 arg2 <<< "$command"
case "$op" in
  put) cp -- "$arg1" "$share_dir/$arg2" ;;
  rename) mv -- "$share_dir/$arg1" "$share_dir/$arg2" ;;
  ls) for f in "$share_dir"/omnidesk-*.tar.gz.age; do [[ -e "$f" ]] && printf '%s N 1 Tue Oct 21 00:00:00 2026\n' "${f##*/}"; done ;;
  del) rm -- "$share_dir/$arg1" ;;
  *) exit 2 ;;
esac
STUB
chmod +x "$tmp/bin/"*

timestamp=$(date -u +%Y%m%dT%H%M%SZ)
export TEST_BACKUP_NAME="$timestamp.test"
export TEST_RECIPIENT="age1$(printf 'a%.0s' {1..58})"
export TEST_NAS="$tmp/nas"
export TEST_LOG="$tmp/worker.log"
export PATH="$tmp/bin:$PATH"
for day in 01 02 03 04 05 06 07 08; do
  : > "$tmp/nas/share/omnidesk-202601${day}T030000Z.tar.gz.age"
done

(cd "$tmp/project" && /bin/bash scripts/backup-nas.sh) >"$tmp/success.log"
[[ -s "$tmp/nas/share/omnidesk-${timestamp}.tar.gz.age" ]]
[[ $(find "$tmp/nas/share" -maxdepth 1 -type f -name 'omnidesk-*.tar.gz.age' | wc -l) -eq 7 ]]
grep -q '^success$' "$tmp/worker.log"

# A transfer failure must not prune any of the existing completed archives.
export TEST_LAST_SUCCESS_AT=''
next_timestamp=$(date -u -d '+1 second' +%Y%m%dT%H%M%SZ)
export TEST_BACKUP_NAME="$next_timestamp.test"
export FAIL_SMB=1
before=$(find "$tmp/nas/share" -maxdepth 1 -type f -name 'omnidesk-*.tar.gz.age' | wc -l)
if (cd "$tmp/project" && /bin/bash scripts/backup-nas.sh) >"$tmp/failure.log" 2>&1; then
  echo 'Expected SMB upload failure.' >&2
  exit 1
fi
after=$(find "$tmp/nas/share" -maxdepth 1 -type f -name 'omnidesk-*.tar.gz.age' | wc -l)
[[ "$before" -eq "$after" ]]
echo 'Backup NAS tests passed.'
