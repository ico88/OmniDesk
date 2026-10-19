#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077
# Local backup only. Archives include private keys and must never be committed or shared publicly.
docker compose config --quiet
mkdir -p .backups
chmod 700 .backups
backup_path=$(mktemp -d ".backups/$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")
printf 'Backup directory: %s\n' "$backup_path"
docker compose exec -T db pg_dump -U omnidesk -d omnidesk --format=custom > "$backup_path/database.dump.partial"
mv -- "$backup_path/database.dump.partial" "$backup_path/database.dump"
docker compose cp api:/keys "$backup_path/identity_keys"
docker compose cp caddy:/data "$backup_path/caddy_data"
cp -- .env "$backup_path/environment"
cp -- .secrets/db_password "$backup_path/db_password"
cp -- compose.yaml "$backup_path/compose.yaml"
mkdir "$backup_path/deploy"
cp -- deploy/Caddyfile deploy/dashboard.Caddyfile "$backup_path/deploy/"
# Tighten copied secret permissions, including files originally mounted world-readable in containers.
chmod -R go-rwx "$backup_path"
docker compose images --format json > "$backup_path/images.json"
git rev-parse HEAD > "$backup_path/source-revision.txt"
printf 'Source revision records HEAD only; preserve working changes separately.\n' > "$backup_path/README.txt"
(cd "$backup_path" && sha256sum database.dump > database.sha256)
touch "$backup_path/READY"
printf 'Backup completed: %s\n' "$backup_path"
