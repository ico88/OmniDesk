#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077
compose=(docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml -f compose.rustdesk-pilot.yaml)

command -v age >/dev/null || { echo 'The age command is required.' >&2; exit 2; }
command -v smbclient >/dev/null || { echo 'The smbclient command is required.' >&2; exit 2; }
command -v python3 >/dev/null || { echo 'The python3 command is required.' >&2; exit 2; }
command -v flock >/dev/null || { echo 'The flock command is required.' >&2; exit 2; }

backup_root=$(bash scripts/prepare-backup-directory.sh .backups)
exec 9>"$backup_root/.offsite.lock"
if ! flock -n 9; then
  echo 'Another off-site backup is already running.' >&2
  exit 1
fi

work_dir=$(mktemp -d "$backup_root/.nas-run.XXXXXX")
chmod 700 "$work_dir"
cleanup() { rm -rf -- "$work_dir"; }
trap cleanup EXIT

stage=configuration
record_failure() {
  local code=$1
  "${compose[@]}" exec -T api dotnet OmniDesk.Api.dll --backup-nas-record-failure "$code" >/dev/null 2>&1 || true
}
on_error() {
  local code=$stage
  trap - ERR
  record_failure "$code"
}
trap on_error ERR

# The API decrypts the persisted settings with its Data Protection keys. stdout
# is captured in a 0600 temporary file and is never written to the journal.
"${compose[@]}" exec -T api dotnet OmniDesk.Api.dll --backup-nas-export >"$work_dir/settings.json"
python3 - "$work_dir/settings.json" "$work_dir/smb.auth" >"$work_dir/runtime.txt" <<'PY'
import json, os, re, sys

with open(sys.argv[1], encoding="utf-8") as f:
    payload = json.load(f)
settings = payload.get("settings")
if not settings or not settings.get("enabled"):
    print("disabled")
    raise SystemExit(0)
fields = ["server", "share", "username", "password", "domain", "ageRecipient", "scheduleTime", "timeZoneId", "keepCount"]
values = [str(settings.get(name, "")) for name in fields]
server, share, username, password, domain, recipient, schedule, timezone, keep = values
if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]{0,252}", server): raise SystemExit("Invalid NAS server setting")
parts = share.strip("/").split("/")
if not parts or any(not re.fullmatch(r"[A-Za-z0-9._-]{1,80}", p) or p in (".", "..") for p in parts): raise SystemExit("Invalid NAS share setting")
if not username or len(username) > 128 or any(c in username for c in "\r\n") or any(c in password for c in "\r\n") or any(c in domain for c in "\r\n"):
    raise SystemExit("Invalid NAS credentials")
if not re.fullmatch(r"age1[0-9a-z]{20,90}", recipient): raise SystemExit("Invalid age recipient")
if not re.fullmatch(r"(?:[01][0-9]|2[0-3]):[0-5][0-9]", schedule): raise SystemExit("Invalid schedule time")
if not re.fullmatch(r"[1-9][0-9]{0,2}", keep) or int(keep) > 365: raise SystemExit("Invalid retention count")
with open(sys.argv[2], "w", encoding="utf-8") as f:
    f.write(f"username = {username}\npassword = {password}\n")
    if domain: f.write(f"domain = {domain}\n")
os.chmod(sys.argv[2], 0o600)
print("\n".join(["enabled", server, "/".join(parts), recipient, schedule, timezone, keep, payload.get("lastSuccessAt") or ""]))
PY
mapfile -t config < "$work_dir/runtime.txt"
[[ "${config[0]:-}" == enabled ]] || { echo 'Scheduled NAS backup is disabled in OmniDesk settings.'; exit 0; }
server=${config[1]}; remote_path=${config[2]}; recipient=${config[3]}; schedule=${config[4]}; timezone=${config[5]}; keep_count=${config[6]}; last_success=${config[7]:-}

if ! TZ="$timezone" date +%H:%M >/dev/null 2>&1; then
  echo 'Configured NAS backup timezone is not available on the host.' >&2
  false
fi
today=$(TZ="$timezone" date +%F)
now_time=$(TZ="$timezone" date +%H:%M)
last_success_day=''
if [[ -n "$last_success" ]]; then last_success_day=$(TZ="$timezone" date -d "$last_success" +%F); fi
if [[ "$today" == "$last_success_day" || "$now_time" < "$schedule" ]]; then
  exit 0
fi

"${compose[@]}" exec -T api dotnet OmniDesk.Api.dll --backup-nas-record-start >/dev/null
stage=backup
bash scripts/backup.sh

latest_backup=''
while IFS= read -r -d '' candidate; do
  [[ -f "$candidate/READY" && ! -L "$candidate/READY" ]] || continue
  if [[ -z "$latest_backup" || "$candidate" -nt "$latest_backup" ]]; then latest_backup=$candidate; fi
done < <(find "$backup_root" -mindepth 1 -maxdepth 1 -type d -name '20*Z.*' -print0)
[[ -n "$latest_backup" ]] || { echo 'No verified local backup was produced.' >&2; false; }
stage=verify
bash scripts/verify-backup.sh "$latest_backup"

backup_name=${latest_backup##*/}
timestamp=${backup_name%%.*}
[[ "$timestamp" =~ ^20[0-9]{6}T[0-9]{6}Z$ ]] || { echo 'Unexpected backup name.' >&2; false; }
local_archive="$work_dir/omnidesk-${timestamp}.tar.gz.age"
tar -C "$backup_root" -czf - "$backup_name" | age -r "$recipient" -o "$local_archive"
[[ -s "$local_archive" && ! -L "$local_archive" ]] || { echo 'Encrypted NAS archive is empty or invalid.' >&2; false; }

remote_directory=${remote_path#*/}
remote_name=${remote_path%%/*}
prefix=''
if [[ "$remote_path" == */* ]]; then prefix="cd $remote_directory; "; fi
final="omnidesk-${timestamp}.tar.gz.age"
partial=".${final}.partial"
stage=upload
smbclient "//$server/$remote_name" -A "$work_dir/smb.auth" -m SMB3 -c "${prefix}put $local_archive $partial"
smbclient "//$server/$remote_name" -A "$work_dir/smb.auth" -m SMB3 -c "${prefix}rename $partial $final"

# List only our strictly named archives; rotate after the new final name exists.
stage=rotation
listing=$(smbclient "//$server/$remote_name" -A "$work_dir/smb.auth" -m SMB3 -c "${prefix}ls")
mapfile -t archives < <(printf '%s\n' "$listing" | awk '$2 == "N" && $1 ~ /^omnidesk-20[0-9]{6}T[0-9]{6}Z\.tar\.gz\.age$/ {print $1}' | sort -r)
for (( index=keep_count; index<${#archives[@]}; index++ )); do
  smbclient "//$server/$remote_name" -A "$work_dir/smb.auth" -m SMB3 -c "${prefix}del ${archives[index]}"
done

stage=record
"${compose[@]}" exec -T api dotnet OmniDesk.Api.dll --backup-nas-record-success >/dev/null
trap - ERR
printf 'Encrypted backup uploaded to NAS; retained %s archive(s).\n' "$keep_count"
