#!/usr/bin/env bash
set -Eeuo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
temp=$(mktemp -d /tmp/omnidesk-backup-verification.XXXXXX)
trap 'rm -rf -- "$temp"' EXIT
mkdir -p "$temp/bin" "$temp/archive/attachments"
cat > "$temp/bin/docker" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ " $* " == *' psql '* ]]; then exit 0; fi
if [[ " $* " == *' pg_restore '* ]]; then exit 0; fi
exit 0
SH
chmod 700 "$temp/bin/docker"
printf 'staged database fixture\n' > "$temp/archive/database.dump"
(cd "$temp/archive" && sha256sum database.dump > database.sha256 && : > attachments.sha256)
(cd "$temp/archive" && find . -type f ! -name files.sha256 ! -name READY -print0 | sort -z | xargs -0 -r sha256sum > files.sha256)

if PATH="$temp/bin:$PATH" bash "$repo/scripts/verify-backup.sh" "$temp/archive" >/dev/null 2>&1; then
  echo 'FAIL: verifier accepted a staged archive without READY.' >&2
  exit 1
fi

PATH="$temp/bin:$PATH" bash "$repo/scripts/verify-backup.sh" --staging "$temp/archive" > "$temp/verify.log"
grep -Fq 'Database restore verified in an isolated temporary PostgreSQL instance.' "$temp/verify.log"
touch "$temp/archive/READY"
PATH="$temp/bin:$PATH" bash "$repo/scripts/verify-backup.sh" "$temp/archive" > "$temp/ready.log"
grep -Fq 'Database restore verified in an isolated temporary PostgreSQL instance.' "$temp/ready.log"
echo 'PASS: backup verifier rejects unmarked archives and verifies staging before READY.'
