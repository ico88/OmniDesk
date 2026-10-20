#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
umask 077
install_docker=false
start=false
for argument in "$@"; do
  case "$argument" in
    --install-docker) install_docker=true ;;
    --start) start=true ;;
    --help) echo 'Usage: ./setup.sh [--install-docker] [--start]'; exit 0 ;;
    *) echo "Unknown argument: $argument" >&2; exit 1 ;;
  esac
done
if [[ ! -f /etc/os-release ]]; then echo 'Ubuntu Server required' >&2; exit 1; fi
source /etc/os-release
if [[ "$ID" != ubuntu ]]; then echo 'Only Ubuntu Server is supported by this installer' >&2; exit 1; fi
if $install_docker; then
  if [[ $EUID -ne 0 ]]; then echo 'Use sudo for --install-docker' >&2; exit 1; fi
  apt-get update
  apt-get install -y ca-certificates curl python3
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  arch=$(dpkg --print-architecture)
  cat > /etc/apt/sources.list.d/docker.sources <<SOURCES
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: ${UBUNTU_CODENAME:-$VERSION_CODENAME}
Components: stable
Architectures: $arch
Signed-By: /etc/apt/keyrings/docker.asc
SOURCES
  apt-get update
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker
fi
command -v python3 >/dev/null || { echo 'Install python3 first' >&2; exit 1; }
command -v docker >/dev/null || { echo 'Docker missing. Rerun with --install-docker only if authorized.' >&2; exit 1; }
command -v curl >/dev/null || { echo 'Install curl first' >&2; exit 1; }
docker compose version >/dev/null

if [[ ! -f .env ]]; then
  domain=${OMNIDESK_DOMAIN:-omnidesk.cricatania.it}
  acme_email=${ACME_EMAIL:-}
  admin_email=${ADMIN_EMAIL:-}
  read -r -p "Public DNS name [$domain]: " answer; domain=${answer:-$domain}
  read -r -p 'Let’s Encrypt email: ' answer; acme_email=${answer:-$acme_email}
  read -r -p 'Initial administrator email: ' answer; admin_email=${answer:-$admin_email}
  DOMAIN_INPUT="$domain" ACME_INPUT="$acme_email" ADMIN_INPUT="$admin_email" python3 - <<'PY'
import os, re
from pathlib import Path
domain = os.environ['DOMAIN_INPUT']
if not re.fullmatch(r'(?=.{1,253}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}', domain):
    raise SystemExit('Invalid DNS name')
for value in (os.environ['ACME_INPUT'], os.environ['ADMIN_INPUT']):
    if not re.fullmatch(r'[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', value):
        raise SystemExit('Invalid email')
Path('.env').write_text(
    f'OMNIDESK_DOMAIN={domain}\nACME_EMAIL={os.environ["ACME_INPUT"]}\n'
    f'ADMIN_EMAIL={os.environ["ADMIN_INPUT"]}\nOMNIDESK_BIND_ADDRESS=10.0.0.53\n'
    'OMNIDESK_HTTP_PORT=80\nOMNIDESK_HTTPS_PORT=443\nOMNIDESK_TOTP_ENABLED=false\n'
    'OMNIDESK_API_IMAGE=omnidesk-api:production\nOMNIDESK_DASHBOARD_IMAGE=omnidesk-dashboard:production\n'
)
PY
fi

python3 - <<'PY'
import re
from pathlib import Path
values = {}
for line in Path('.env').read_text().splitlines():
    if line and not line.startswith('#') and '=' in line:
        key, value = line.split('=', 1)
        values[key] = value
expected = {
    'OMNIDESK_DOMAIN': 'omnidesk.cricatania.it',
    'OMNIDESK_BIND_ADDRESS': '10.0.0.53',
    'OMNIDESK_HTTP_PORT': '80',
    'OMNIDESK_HTTPS_PORT': '443',
    'OMNIDESK_TOTP_ENABLED': 'false',
    'OMNIDESK_API_IMAGE': 'omnidesk-api:production',
    'OMNIDESK_DASHBOARD_IMAGE': 'omnidesk-dashboard:production',
}
for key, value in expected.items():
    if values.get(key) != value:
        raise SystemExit(f'{key} in .env must be {value}; review this host configuration before deployment.')
if any(key in values for key in ('OMNIDESK_LAN_IP',)):
    raise SystemExit('Remove legacy LAN settings from .env before deployment.')
for key in ('ACME_EMAIL', 'ADMIN_EMAIL'):
    if not re.fullmatch(r'[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', values.get(key, '')):
        raise SystemExit(f'{key} in .env must be a valid email address.')
PY

mkdir -p .secrets
chmod 700 .secrets
python3 - <<'PY'
import secrets
from pathlib import Path
for name in ('db_password', 'db_runtime_password'):
    p = Path('.secrets') / name
    if not p.exists():
        p.write_text(secrets.token_hex(32))
    p.chmod(0o444)
PY

compose=(docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml)
"${compose[@]}" config --quiet
if ! $start; then
  echo 'Public Caddy configuration ready. Review .env; no containers were started. Run ./setup.sh --start to deploy.'
  exit 0
fi
docker info >/dev/null
domain=$(sed -n 's/^OMNIDESK_DOMAIN=//p' .env)
python3 - <<'PY'
import errno, os, socket
for port in (80, 443):
    probe = socket.socket()
    try:
        probe.bind(('10.0.0.53', port))
    except OSError as exc:
        if exc.errno == errno.EACCES and os.geteuid() != 0:
            print(f'Cannot preflight privileged port {port} as an unprivileged user; Docker will verify it when starting Caddy.')
            continue
        raise SystemExit(f'Cannot bind 10.0.0.53:{port}: {exc}. Check address and existing listeners.')
    finally:
        probe.close()
PY
python3 - "$domain" <<'PY'
import ipaddress, socket, sys
try:
    records = socket.getaddrinfo(sys.argv[1], 443, type=socket.SOCK_STREAM)
    addresses = {ipaddress.ip_address(item[4][0]) for item in records}
except OSError:
    addresses = set()
normalized = {str(address.ipv4_mapped if address.version == 6 else address) for address in addresses}
if '151.3.161.202' not in normalized:
    raise SystemExit('Public DNS must resolve to 151.3.161.202 before deployment.')
if any(address.version == 6 and address.ipv4_mapped is None for address in addresses):
    raise SystemExit('An IPv6 record is present but this installation is configured for IPv4 only; configure IPv6 end-to-end or remove the AAAA record.')
print('DNS resolves to: ' + ', '.join(sorted(normalized)))
PY

"${compose[@]}" build api dashboard
"${compose[@]}" up -d db keys-init
"${compose[@]}" run --rm migrate
user_count=$("${compose[@]}" exec -T db psql -U omnidesk -d omnidesk -tAc 'SELECT COUNT(*) FROM "AspNetUsers"')
if [[ "$user_count" == 0 ]]; then
  python3 - <<'PY'
import secrets
from pathlib import Path
p = Path('.secrets/bootstrap_password')
if not p.exists():
    p.write_text('Od!9' + secrets.token_urlsafe(24))
p.chmod(0o444)
PY
  "${compose[@]}" run --rm bootstrap
  echo 'Initial password is in .secrets/bootstrap_password. Transfer it through a private channel, then securely remove the file.'
fi
"${compose[@]}" up -d api dashboard caddy
"${compose[@]}" ps
ready=false
for attempt in {1..30}; do
  if curl --silent --show-error --fail --max-time 5 --resolve "${domain}:443:10.0.0.53" "https://${domain}/api/health/ready" >/dev/null; then ready=true; break; fi
  sleep 2
done
if ! $ready; then
  echo 'Public HTTPS readiness check failed. Inspect: docker compose -p omnidesk-production -f compose.yaml -f compose.direct.yaml logs --tail=100 api caddy' >&2
  exit 1
fi
redirect=$(curl --silent --show-error --max-time 5 --output /dev/null --write-out '%{redirect_url}' --resolve "${domain}:80:10.0.0.53" "http://${domain}/")
if [[ "$redirect" != "https://${domain}/" ]]; then
  echo 'HTTP to HTTPS redirect check failed.' >&2
  exit 1
fi
curl --silent --show-error --fail --max-time 5 --resolve "${domain}:443:10.0.0.53" "https://${domain}/" >/dev/null
echo "Deployment ready: https://${domain}/api/health/ready"
