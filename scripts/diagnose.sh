#!/usr/bin/env bash
set -Eeuo pipefail
domain=${1:-omnidesk.cricatania.it}
printf 'Checking public DNS and HTTPS for %s\n' "$domain"
python3 - "$domain" <<'PY'
import socket, sys
try:
    addresses = sorted({item[4][0] for item in socket.getaddrinfo(sys.argv[1], 443, type=socket.SOCK_STREAM)})
except OSError as exc:
    raise SystemExit(f'DNS lookup failed: {exc}')
if not addresses:
    raise SystemExit('DNS returned no addresses')
print('DNS addresses: ' + ', '.join(addresses))
PY
curl --fail --show-error --silent --max-time 10 "https://$domain/api/health/live"
printf '\n'
curl --fail --show-error --silent --max-time 10 "https://$domain/api/health/ready"
printf '\n'
curl --fail --show-error --silent --max-time 10 --output /dev/null --write-out 'Dashboard HTTPS status: %{http_code}; certificate: %{ssl_verify_result}\n' "https://$domain/"

