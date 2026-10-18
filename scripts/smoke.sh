#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export SMOKE_SECRETS
SMOKE_SECRETS=$(mktemp -d /tmp/omnidesk-smoke.XXXXXX)
# No production ports/volumes. Secrets remain in a private temporary directory.
trap 'docker compose -f compose.smoke.yaml logs --no-color api caddy > "$SMOKE_SECRETS/runtime.log"; docker compose -f compose.smoke.yaml down' EXIT
python3 - <<'PY'
from pathlib import Path
import os, secrets
for name in ('db_password','bootstrap_password'):
    p=Path(os.environ['SMOKE_SECRETS'])/name
    p.write_text('Od!9'+secrets.token_urlsafe(32))
    p.chmod(0o444)
PY
docker compose -f compose.smoke.yaml up -d db
docker compose -f compose.smoke.yaml run --rm migrate
docker compose -f compose.smoke.yaml run --rm bootstrap
if docker compose -f compose.smoke.yaml run --rm bootstrap > "$SMOKE_SECRETS/bootstrap-retry.log" 2>&1; then
  echo 'ERROR: bootstrap unexpectedly accepted a second invocation' >&2; exit 1
fi
docker compose -f compose.smoke.yaml up -d api dashboard caddy
docker compose -f compose.smoke.yaml run --rm probe
