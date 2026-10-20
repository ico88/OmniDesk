#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export SMOKE_SECRETS
SMOKE_SECRETS=$(mktemp -d /tmp/omnidesk-smoke.XXXXXX)
# No production ports/volumes. Secrets remain in a private temporary directory.
trap 'docker compose -f compose.smoke.yaml logs --no-color api caddy > "$SMOKE_SECRETS/runtime.log"; docker compose -f compose.smoke.yaml down' EXIT
smoke_psql() {
  docker compose -f compose.smoke.yaml exec -T db sh -c 'PGPASSWORD="$(cat /run/secrets/db_password)" psql -h 127.0.0.1 -U omnidesk -d omnidesk -v ON_ERROR_STOP=1 "$@"' sh "$@"
}
python3 - <<'PY'
from pathlib import Path
import os, secrets, json, hashlib, zipfile
for name in ('db_password','bootstrap_password'):
    p=Path(os.environ['SMOKE_SECRETS'])/name
    p.write_text('Od!9'+secrets.token_urlsafe(32))
    p.chmod(0o444)
email_key=Path(os.environ['SMOKE_SECRETS'])/'email_intake_key'
email_key.write_bytes(secrets.token_bytes(32))
email_key.chmod(0o444)
folder=Path(os.environ['SMOKE_SECRETS'])/'downloads'
folder.mkdir(mode=0o755)
package=folder/'OmniDesk.Windows.LAN-0.0.1.zip'
with zipfile.ZipFile(package,'w') as archive:
    archive.writestr('fixture.txt','Isolated browser download fixture; not an installer')
package.chmod(0o444)
manifest=folder/'windows-release.json'
manifest.write_text(json.dumps(dict(version='0.0.1',commit='a'*40,sha256=hashlib.sha256(package.read_bytes()).hexdigest(),size=package.stat().st_size,fileName=package.name,remoteEngineIncluded=False)))
manifest.chmod(0o444)
PY
docker compose -f compose.smoke.yaml up -d db
docker compose -f compose.smoke.yaml run --rm migrate
docker compose -f compose.smoke.yaml run --rm bootstrap
if docker compose -f compose.smoke.yaml run --rm bootstrap > "$SMOKE_SECRETS/bootstrap-retry.log" 2>&1; then
  echo 'ERROR: bootstrap unexpectedly accepted a second invocation' >&2; exit 1
fi
docker compose -f compose.smoke.yaml up -d api dashboard caddy
docker compose -f compose.smoke.yaml run --rm probe
docker compose -f compose.smoke.yaml --profile verification run --rm clamav-probe

if [[ "${1:-}" == --browser ]]; then
  docker compose -f compose.smoke.yaml run --rm browser

  # Exercise the actual hosted SLA breach/escalation/notification workers using
  # a disposable ticket and a backdated persisted breach, not wall-clock waits.
  escalation_target=$(smoke_psql -Atc 'SELECT s."Id" FROM "SupportTicketSlaTargets" s JOIN "SupportTickets" t ON t."Id" = s."TicketId" WHERE s."PolicyVersion" = 3 AND s."TargetType" = '\''resolution'\'' AND s."CompletedAt" IS NULL AND s."BreachedAt" IS NULL AND t."State" = '\''in_progress'\'' ORDER BY t."CreatedAt" DESC LIMIT 1')
  [[ -n "$escalation_target" ]] || { echo 'No open version-3 SLA target is available for the isolated worker probe.' >&2; exit 1; }
  smoke_psql -c "UPDATE \"SupportTicketSlaTargets\" SET \"DueAt\" = now() - interval '1 minute' WHERE \"Id\" = '$escalation_target' AND \"CompletedAt\" IS NULL AND \"BreachedAt\" IS NULL" >/dev/null
  breach_seen=false
  for _ in $(seq 1 30); do
    breached=$(smoke_psql -Atc "SELECT (\"BreachedAt\" IS NOT NULL)::text FROM \"SupportTicketSlaTargets\" WHERE \"Id\" = '$escalation_target'")
    if [[ "$breached" == true ]]; then breach_seen=true; break; fi
    sleep 2
  done
  [[ "$breach_seen" == true ]] || { echo 'SLA breach worker did not persist the overdue target.' >&2; exit 1; }
  smoke_psql -c "UPDATE \"SupportTicketSlaTargets\" SET \"BreachedAt\" = now() - interval '16 minutes' WHERE \"Id\" = '$escalation_target'" >/dev/null
  notification_seen=false
  for _ in $(seq 1 30); do
    notifications=$(smoke_psql -Atc "SELECT count(*) FROM \"UserNotifications\" n JOIN \"CoreOutboxEvents\" e ON e.\"Id\" = n.\"EventId\" WHERE e.\"AggregateId\" = (SELECT \"TicketId\" FROM \"SupportTicketSlaTargets\" WHERE \"Id\" = '$escalation_target') AND e.\"EventType\" = 'ticket.sla.escalated' AND e.\"Payload\"->>'stepMinutes' = '15'")
    if [[ "$notifications" -gt 0 ]]; then notification_seen=true; break; fi
    sleep 2
  done
  [[ "$notification_seen" == true ]] || { echo 'SLA escalation/outbox notification worker did not deliver the configured first step.' >&2; exit 1; }
  echo 'PASS: hosted SLA breach, persisted escalation and scoped notification workers delivered an overdue fixture.'

  # Build a secret-bearing archive from this disposable stack, including the
  # portal attachment created by Chromium, then exercise the real restore path.
  restore_fixture="$SMOKE_SECRETS/restore-fixture"
  mkdir -m 700 "$restore_fixture"
  docker compose -f compose.smoke.yaml exec -T db pg_dump -U omnidesk -d omnidesk --format=custom > "$restore_fixture/database.dump"
  api_container=$(docker compose -f compose.smoke.yaml ps -q api)
  caddy_container=$(docker compose -f compose.smoke.yaml ps -q caddy)
  attachment_files=$(docker exec "$api_container" sh -ec 'find /attachments -maxdepth 1 -type f | wc -l')
  [[ "$attachment_files" -gt 0 ]] || { echo 'Smoke database has attachments but API blob storage is empty.' >&2; exit 1; }
  mkdir "$restore_fixture/identity_keys" "$restore_fixture/attachments" "$restore_fixture/caddy_data"
  docker exec --user 1654 "$api_container" tar -C /keys -cf - . | tar -C "$restore_fixture/identity_keys" -xf -
  docker exec --user 1654 "$api_container" tar -C /attachments -cf - . | tar -C "$restore_fixture/attachments" -xf -
  docker exec --user 0 "$caddy_container" tar -C /data -cf - . | tar -C "$restore_fixture/caddy_data" -xf -
  cp -- "$SMOKE_SECRETS/db_password" "$restore_fixture/db_password"
  chmod -R go-rwx "$restore_fixture"
  (
    cd "$restore_fixture"
    sha256sum database.dump > database.sha256
    find attachments -type f -print0 | sort -z | xargs -0 -r sha256sum > attachments.sha256
    find . -type f ! -name files.sha256 ! -name READY -print0 | sort -z | xargs -0 -r sha256sum > files.sha256
  )
  bash scripts/verify-backup.sh --staging "$restore_fixture"
  touch "$restore_fixture/READY"
fi
