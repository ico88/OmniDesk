#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077

if [[ $# -ne 1 || ! "$1" =~ ^[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]]; then
  echo "Usage: $0 administrator-email" >&2
  exit 2
fi
email=$1
cleanup() { unset password confirmation; }
trap cleanup EXIT HUP INT TERM

read -r -s -p 'Nuova password (minimo 14 caratteri): ' password; printf '\n'
read -r -s -p 'Ripeti la nuova password: ' confirmation; printf '\n'
if [[ ${#password} -lt 14 || ${#password} -gt 1024 || "$password" != "$confirmation" ]]; then
  echo 'Password non valida o le due password non coincidono.' >&2
  exit 1
fi
printf '%s' "$password" | docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml run \
  --rm --no-deps -T api --reset-admin-password "$email" -
unset password confirmation
