#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export LOCAL_UID="$(id -u)" LOCAL_GID="$(id -g)"
# Dedicated project; PostgreSQL data is tmpfs and no production secrets/ports are used.
trap 'docker compose -f compose.test.yaml down' EXIT
docker compose -f compose.test.yaml up --abort-on-container-exit --exit-code-from tests
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD/src/dashboard:/source:ro" node:22-alpine sh -c 'mkdir -p /tmp/dashboard && tar -C /source --exclude=./node_modules --exclude=./dist -cf - . | tar -C /tmp/dashboard -xf - && cd /tmp/dashboard && npm ci && npm run build && npm test'
