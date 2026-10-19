#!/usr/bin/env python3
"""Read-only HTTPS runtime checks; never logs in or changes application data."""
import argparse
import json
import re
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', default='https://192.168.1.233:8085')
    parser.add_argument('--ca', default='omnidesk-local-ca.crt')
    args = parser.parse_args()
    origin = urllib.parse.urlsplit(args.server)
    if (origin.scheme != 'https' or not origin.hostname or origin.username or origin.password
            or origin.path not in ('', '/') or origin.query or origin.fragment):
        parser.error('--server deve essere una origine HTTPS senza credenziali o percorso')
    base = args.server.rstrip('/')

    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None

    client = urllib.request.build_opener(
        urllib.request.ProxyHandler({}), NoRedirect(),
        urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=args.ca)))

    def get(path, expected=200):
        try:
            response = client.open(base + path, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            if response.status != expected:
                raise RuntimeError(f'{path}: HTTP {response.status}, atteso {expected}')
            body = response.read(4 * 1024 * 1024 + 1)
            if len(body) > 4 * 1024 * 1024:
                raise RuntimeError(f'{path}: risposta troppo grande')
            return body, response.headers

    ready, _ = get('/api/health/ready')
    if json.loads(ready).get('status') != 'ready':
        raise RuntimeError('Readiness non confermata')
    print('PASS readiness HTTPS e verifica CA')
    html, headers = get('/')
    if 'text/html' not in headers.get('Content-Type', ''):
        raise RuntimeError('Dashboard non HTML')
    assets = re.findall(r'(?:src|href)="(/assets/[^"<> ]+\.(?:js|css))"', html.decode())
    if not assets:
        raise RuntimeError('Asset dashboard mancanti')
    for asset in sorted(set(assets)):
        if not get(asset)[0]:
            raise RuntimeError('Asset dashboard vuoto')
    print('PASS dashboard e asset pubblicati')
    get('/api/auth/me', 401)
    print('PASS sessione anonima rifiutata')
    print('Verifica limitata: nessun login, mutazione, remoto o test Windows eseguito.')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print(f'FAIL verifica runtime: {error}', file=sys.stderr)
        sys.exit(1)
