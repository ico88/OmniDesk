#!/usr/bin/env python3
"""Package the verified Windows artifact for the public HTTPS OmniDesk server; no credentials."""
import argparse
import hashlib
import json
import uuid
from pathlib import Path
import re
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--artifact', type=Path, required=True)
parser.add_argument('--run', required=True)
parser.add_argument('--commit', required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--version')
parser.add_argument('--remote-engine', action='store_true')
args = parser.parse_args()
if args.version is None:
    match = re.fullmatch(r'windows-([0-9]+\.[0-9]+\.[0-9]+)', args.artifact.name)
    if match is None:
        parser.error('Specificare --version o usare una directory windows-X.Y.Z')
    args.version = match.group(1)
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
files['LEGGIMI.txt'] = f'''OmniDesk Agent + Tray - Windows x64
Origine: https://github.com/ico88/OmniDesk/actions/runs/{args.run}
Commit: {args.commit}
Server: https://omnidesk.cricatania.it

Pacchetto unsigned per laboratorio. I checksum non sostituiscono una firma.
Non include credenziali, token o chiavi private.
{remote_note}

1. Estrai il kit su Windows e verifica i checksum dell'artifact GitHub.
2. Su un nuovo dispositivo, dalla console OmniDesk apri Dispositivi > Registra
   un dispositivo e genera un token monouso. Non salvarlo in un file. Se il PC
   è già registrato sullo stesso server, non serve un nuovo token: Setup conserva
   l'identità esistente durante l'aggiornamento.
3. Avvia OmniDesk.Setup.exe con autorizzazione amministrativa. Incolla il token
   nel campo mascherato solo per un nuovo dispositivo. Setup verifica il
   certificato HTTPS tramite le autorità pubbliche attendibili di Windows e la
   readiness del server, installa il pacchetto e invia l'eventuale token su pipe
   standard, mai in argomenti o file. Nessuna CA aggiuntiva viene installata.
4. La Tray si avvia al prossimo accesso dell'utente; il servizio è indipendente.

Rimozione: Impostazioni Windows > App > OmniDesk Agent > Disinstalla. Prima della
rimozione il programma revoca identità e autorizzazioni remote sul server. Se il
server non è raggiungibile, Windows Installer interrompe la disinstallazione per
non lasciare valide le credenziali server.
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
