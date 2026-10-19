#!/usr/bin/env python3
"""Package the verified GitHub Windows artifact and public LAN CA; no credentials."""
import argparse
import hashlib
import json
import uuid
from pathlib import Path
import re
import ssl
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--artifact', type=Path, required=True)
parser.add_argument('--run', required=True)
parser.add_argument('--commit', required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--version', default='0.6.0')
parser.add_argument('--remote-engine', action='store_true')
parser.add_argument('--ca', type=Path, default=Path('omnidesk-local-ca.crt'))
args = parser.parse_args()
if not args.run.isdigit() or not re.fullmatch('[a-f0-9]{40}', args.commit):
    parser.error('Run e commit GitHub non validi')
if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', args.version) or args.artifact.name != 'windows-' + args.version:
    parser.error('Versione e directory artifact non corrispondenti')
if args.output.name != f'OmniDesk.Windows.LAN-{args.version}.zip':
    parser.error('Nome output non corrispondente alla versione')
remote_note = ('Include il motore remoto Windows; attivarlo dalla console IT dopo enrollment. Richiede consenso e collaudo Windows.'
               if args.remote_engine else 'Non include MeshCentral Agent: il desktop remoto completo non e disponibile con questo pacchetto.')
expected = {}
for line in (args.artifact / 'SHA256SUMS.txt').read_text().splitlines():
    digest, name = line.split(None, 1)
    name = name.strip()
    if name not in ('OmniDesk.Agent.msi', 'Install-OmniDeskAgent.ps1') or name in expected or not re.fullmatch('[a-fA-F0-9]{64}', digest):
        parser.error('Manifest artifact non valido')
    expected[name] = digest.lower()
if len(expected) != 2:
    parser.error('Manifest artifact incompleto')
files = {}
for name, digest in expected.items():
    data = (args.artifact / name).read_bytes()
    if hashlib.sha256(data).hexdigest() != digest:
        parser.error('Checksum artifact non corrispondente: ' + name)
    files[name] = data
if not files['OmniDesk.Agent.msi'].startswith(bytes.fromhex('D0CF11E0A1B11AE1')):
    parser.error('Formato MSI non riconosciuto')
ca = args.ca.read_text()
if 'PRIVATE KEY' in ca or ca.count('BEGIN CERTIFICATE') != 1:
    parser.error('Attesa solo la CA pubblica')
fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(ca)).hexdigest().upper()
if fingerprint != '45926C7ACA3D49E6619E377AD0FA2110951F6F0E54DF9B83A6C6E3AD712BE5B2':
    parser.error('CA diversa da quella LAN concordata; verificarla prima di cambiare il pin')
files['omnidesk-local-ca.crt'] = ca.encode()
files['LEGGIMI.txt'] = f'''OmniDesk Agent + Tray - collaudo LAN x64
Origine: https://github.com/ico88/OmniDesk/actions/runs/{args.run}
Commit: {args.commit}

Pacchetto unsigned per laboratorio. I checksum non sostituiscono una firma.
Non include credenziali, token o chiavi private.
{remote_note}

1. Estrai il kit su Windows. Verifica SHA256SUMS.txt contro l'artifact GitHub.
2. Dalla console https://192.168.1.233:8085 genera un token enrollment.
   Crea enrollment-token.txt e proteggilo con ACL SYSTEM/Administrators,
   seguendo docs/windows-lan-install.md. Non inviare il token in chat.
3. Da PowerShell amministrativa, nella directory estratta:

.\\Install-OmniDeskAgent.ps1 -MsiPath .\\OmniDesk.Agent.msi -ServerUrl https://192.168.1.233:8085 -CaCertificatePath .\\omnidesk-local-ca.crt -CaSha256 {fingerprint} -TrustServerCa -AllowUnsignedForTesting -MsiSha256 {expected['OmniDesk.Agent.msi']} -EnrollmentTokenFile .\\enrollment-token.txt

4. Elimina il file token originale dopo enrollment. Esegui logoff/logon per
   avviare la Tray, oppure avviala dalla sessione standard senza elevazione.
5. Verifica heartbeat, stato Tray, ticket senza screenshot, screenshot con
   anteprima/consenso e risposta IT. Chiudere la Tray deve lasciare il servizio
   attivo. Le prove interattive/LAN non sono certificate dalla build CI.

Dismissione: revoca prima il dispositivo sulla console server; poi un
amministratore Windows disinstalla l'MSI. La CA resta installata intenzionalmente.
Nessuna protezione assoluta contro amministratori locali o accesso fisico.
'''.encode()
files['SHA256SUMS.txt'] = ''.join(f'{hashlib.sha256(data).hexdigest()}  {name}\n' for name, data in sorted(files.items())).encode()
args.output.parent.mkdir(parents=True, exist_ok=True)
temporary = args.output.with_name(args.output.name + '.' + uuid.uuid4().hex + '.tmp')
try:
    with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            archive.writestr(name, data)
    temporary.replace(args.output)
finally:
    temporary.unlink(missing_ok=True)
release = dict(version=args.version, commit=args.commit, sha256=hashlib.sha256(args.output.read_bytes()).hexdigest(),
               size=args.output.stat().st_size, fileName=args.output.name, remoteEngineIncluded=args.remote_engine)
metadata = args.output.parent / ('windows-release.' + uuid.uuid4().hex + '.tmp')
metadata.write_text(json.dumps(release, indent=2) + '\n')
metadata.replace(args.output.parent / 'windows-release.json')
print(f'Kit creato: {args.output}')
print('SHA256 ZIP:', hashlib.sha256(args.output.read_bytes()).hexdigest())
