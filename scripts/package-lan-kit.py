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
parser.add_argument('--version', default='0.8.2')
parser.add_argument('--remote-engine', action='store_true')
parser.add_argument('--ca', type=Path, default=Path('omnidesk-local-ca.crt'))
args = parser.parse_args()
if not args.run.isdigit() or not re.fullmatch('[a-f0-9]{40}', args.commit):
    parser.error('Run e commit GitHub non validi')
if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', args.version) or args.artifact.name != 'windows-' + args.version:
    parser.error('Versione e directory artifact non corrispondenti')
if args.output.name != f'OmniDesk.Windows.LAN-{args.version}.zip':
    parser.error('Nome output non corrispondente alla versione')
remote_note = ("Include il motore remoto Windows; abilitarlo dalla console IT dopo enrollment. L'accesso non presidiato resta disattivato per default."
               if args.remote_engine else 'Non include MeshCentral Agent: il desktop remoto non e disponibile con questo pacchetto.')
expected = {}
for line in (args.artifact / 'SHA256SUMS.txt').read_text().splitlines():
    digest, name = line.split(None, 1)
    name = name.strip()
    if name not in ('OmniDesk.Agent.msi', 'OmniDesk.Setup.exe') or name in expected or not re.fullmatch('[a-fA-F0-9]{64}', digest):
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
if not files['OmniDesk.Setup.exe'].startswith(bytes.fromhex('4D5A')):
    parser.error('Setup nativo non riconosciuto')
ca = args.ca.read_text()
if 'PRIVATE KEY' in ca or ca.count('BEGIN CERTIFICATE') != 1:
    parser.error('Attesa solo la CA pubblica')
fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(ca)).hexdigest().upper()
if fingerprint != '45926C7ACA3D49E6619E377AD0FA2110951F6F0E54DF9B83A6C6E3AD712BE5B2':
    parser.error('CA diversa da quella LAN concordata; verificarla prima di cambiare il pin')
files['omnidesk-local-ca.crt'] = ca.encode()
files['LEGGIMI.txt'] = f'''OmniDesk Agent + Tray - installazione LAN x64
Origine: https://github.com/ico88/OmniDesk/actions/runs/{args.run}
Commit: {args.commit}

Pacchetto unsigned per laboratorio. I checksum non sostituiscono una firma.
Non include credenziali, token o chiavi private.
{remote_note}

1. Estrai il kit su Windows e verifica i checksum dell'artifact GitHub.
2. Dalla console OmniDesk, Dispositivi > Registra un dispositivo, genera un
   token monouso. Non salvarlo in un file.
3. Avvia OmniDesk.Setup.exe e incolla il token nel campo mascherato. Windows
   richiede l'autorizzazione amministrativa. Setup verifica CA/HTTPS/server,
   installa il pacchetto e invia il token su pipe standard, mai in argomenti o file.
4. La Tray si avvia al prossimo accesso dell'utente; il servizio è indipendente.

Rimozione: Impostazioni Windows > App > OmniDesk Agent > Disinstalla. Prima della
rimozione il programma revoca identità e autorizzazioni remote sul server. Se il
server non è raggiungibile, Windows Installer interrompe la disinstallazione per
non lasciare valide le credenziali server. La CA resta installata intenzionalmente.
Nessuna protezione assoluta contro amministratori locali o accesso fisico privilegiato.
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
               size=args.output.stat().st_size, fileName=args.output.name, remoteEngineIncluded=args.remote_engine,
               msiSha256=hashlib.sha256(files['OmniDesk.Agent.msi']).hexdigest(), msiSize=len(files['OmniDesk.Agent.msi']))
metadata = args.output.parent / ('windows-release.' + uuid.uuid4().hex + '.tmp')
metadata.write_text(json.dumps(release, indent=2) + '\n')
metadata.replace(args.output.parent / 'windows-release.json')
print(f'Kit creato: {args.output}')
print('SHA256 ZIP:', hashlib.sha256(args.output.read_bytes()).hexdigest())
