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
  # Explicit opt-in: installs packages and starts the Docker system service.
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
docker compose version >/dev/null
if [[ ! -f .env ]]; then
  read -r -p 'Public DNS name (e.g. rmm.example.com): ' domain
  read -r -p 'Let’s Encrypt email: ' acme_email
  read -r -p 'Administrator email: ' admin_email
  DOMAIN_INPUT="$domain" ACME_INPUT="$acme_email" ADMIN_INPUT="$admin_email" python3 - <<'PY'
import os, re
from pathlib import Path
domain = os.environ['DOMAIN_INPUT']
if not re.fullmatch(r'(?=.{1,253}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}', domain):
    raise SystemExit('Invalid DNS name')
for value in (os.environ['ACME_INPUT'], os.environ['ADMIN_INPUT']):
    if not re.fullmatch(r'[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}', value):
        raise SystemExit('Invalid email')
Path('.env').write_text(f'OMNIDESK_DOMAIN={domain}\nACME_EMAIL={os.environ["ACME_INPUT"]}\nADMIN_EMAIL={os.environ["ADMIN_INPUT"]}\n')
PY
fi
mkdir -p .secrets
chmod 700 .secrets
python3 - <<'PY'
import secrets
from pathlib import Path
p = Path('.secrets/db_password')
if not p.exists():
    p.write_text(secrets.token_hex(32))
    p.chmod(0o444)
PY
chmod 444 .secrets/db_password
# Parent directory stays 0700; container users can read their individually mounted secret.
docker compose config --quiet
if ! $start; then
  echo 'Configuration ready. No containers started. Review .env, DNS and ports 80/443, then run ./setup.sh --start.'
  exit 0
fi
docker info >/dev/null
docker compose build api dashboard
docker compose up -d db keys-init
docker compose run --rm migrate
# Bootstrap only an empty DB; never overwrite a password when rerunning setup.
user_count=$(docker compose exec -T db psql -U omnidesk -d omnidesk -tAc 'SELECT COUNT(*) FROM "AspNetUsers"')
if [[ "$user_count" == 0 ]]; then
  python3 - <<'PY'
import secrets
from pathlib import Path
p = Path('.secrets/bootstrap_password')
if not p.exists():
    p.write_text('Od!9' + secrets.token_urlsafe(24))
    p.chmod(0o444)
PY
  docker compose run --rm bootstrap
  echo 'Initial password is in .secrets/bootstrap_password. Transfer it securely, then delete that file.'
fi
docker compose up -d api dashboard caddy
echo 'Containers started. Check docker compose ps/logs and HTTPS readiness. No firewall rules were changed by this script.'
