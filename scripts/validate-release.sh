#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
# Uses existing images. No deploy, rebuild, production migrations or bootstrap.
docker compose config --quiet
docker compose -f compose.test.yaml config --quiet
SMOKE_SECRETS=/tmp/omnidesk-validation-config docker compose -f compose.smoke.yaml config --quiet
BACKUP_CHECK_FILE=/tmp/omnidesk-validation-config.dump docker compose -f compose.backup-check.yaml config --quiet
bash scripts/test.sh
bash scripts/smoke.sh --browser
python3 scripts/verify-lan.py "$@"
