#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077
# Operate on the same stack used by the production server. Keep RustDesk in
# the resolved project so its persistent server identity is included below.
compose=(docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml -f compose.rustdesk-pilot.yaml)
# Local backup only. Archives include private keys and must never be committed or shared publicly.
backup_retention_days=${OMNIDESK_BACKUP_RETENTION_DAYS:-}
if [[ -z "$backup_retention_days" && -f .env ]]; then
  backup_retention_days=$(awk -F= '$1 == "OMNIDESK_BACKUP_RETENTION_DAYS" { print substr($0, index($0, "=") + 1); exit }' .env)
fi
backup_retention_days=${backup_retention_days:-0}
bash scripts/prune-backups.sh --validate-only "$backup_retention_days"
backup_root=$(bash scripts/prepare-backup-directory.sh .backups)
"${compose[@]}" config --quiet
backup_path=$(mktemp -d "$backup_root/$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")
printf 'Backup directory: %s\n' "$backup_path"
"${compose[@]}" exec -T db pg_dump -U omnidesk -d omnidesk --format=custom > "$backup_path/database.dump.partial"
mv -- "$backup_path/database.dump.partial" "$backup_path/database.dump"
"${compose[@]}" cp api:/keys "$backup_path/identity_keys"
attachment_table=$("${compose[@]}" exec -T db psql -U omnidesk -d omnidesk -Atc \
  "SELECT to_regclass('public.\"SupportAttachments\"') IS NOT NULL")
api_container=$("${compose[@]}" ps -q api)
[[ -n "$api_container" ]] || { echo 'Cannot verify attachment storage: API container is not running' >&2; exit 1; }
attachment_mount=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/attachments"}}{{.Name}}{{end}}{{end}}' "$api_container")
if [[ "$attachment_table" == t ]]; then
  [[ -n "$attachment_mount" ]] || { echo 'Attachment table exists but persistent /attachments volume is not mounted' >&2; exit 1; }
  "${compose[@]}" cp api:/attachments "$backup_path/attachments"
elif [[ "$attachment_table" == f && -z "$attachment_mount" ]]; then
  # Older releases predate ticket attachments and their named volume. The
  # database has no references and no blob mount exists to preserve.
  mkdir -- "$backup_path/attachments"
else
  echo 'Attachment schema/storage are inconsistent; refusing an incomplete backup' >&2
  exit 1
fi
(
  cd -- "$backup_path"
  find attachments -type f -print0 | sort -z | xargs -0 -r sha256sum > attachments.sha256
)
"${compose[@]}" cp caddy:/data "$backup_path/caddy_data"
if "${compose[@]}" ps --status running --services | rg -qx meshcentral; then
  "${compose[@]}" cp meshcentral:/opt/meshcentral/meshcentral-data "$backup_path/mesh_data"
  "${compose[@]}" cp meshcentral:/opt/meshcentral/meshcentral-files "$backup_path/mesh_files"
  cp -- .secrets/mesh_password "$backup_path/mesh_password"
  cp -- compose.mesh.yaml compose.mesh-lan.yaml "$backup_path/"
  cp -- deploy/mesh-lan.Caddyfile "$backup_path/deploy.mesh-lan.Caddyfile"
fi
if "${compose[@]}" ps --status running --services | rg -qx rustdesk; then
  "${compose[@]}" cp rustdesk:/data "$backup_path/rustdesk_data"
  cp -- compose.rustdesk-pilot.yaml "$backup_path/"
fi
cp -- .env "$backup_path/environment"
cp -- .secrets/db_password "$backup_path/db_password"
cp -- compose.yaml compose.direct.yaml "$backup_path/"
mkdir "$backup_path/deploy"
cp -- deploy/Caddyfile deploy/dashboard.Caddyfile "$backup_path/deploy/"
# Tighten copied secret permissions, including files originally mounted world-readable in containers.
chmod -R go-rwx "$backup_path"
"${compose[@]}" images --format json > "$backup_path/images.json"
git rev-parse HEAD > "$backup_path/source-revision.txt"
printf 'Source revision records HEAD only; preserve working changes separately.\n' > "$backup_path/README.txt"
(cd "$backup_path" && sha256sum database.dump > database.sha256)
# Hash every regular file after collection. Checksums detect accidental damage,
# but the manifest is not signed and does not authenticate the archive.
if find "$backup_path" -type l -print -quit | rg -q .; then
  echo 'Backup contains an unsupported symbolic link' >&2
  exit 1
fi
(
  cd "$backup_path"
  find . -type f ! -name files.sha256 ! -name READY -print0 | sort -z | xargs -0 -r sha256sum > files.sha256
)
# READY means DB/blob checks and the isolated application restore have passed.
bash scripts/verify-backup.sh --staging "$backup_path"
touch "$backup_path/READY"
printf 'Backup verified and completed: %s\n' "$backup_path"
bash scripts/prune-backups.sh "$backup_root" "$backup_retention_days"
