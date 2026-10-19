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
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--artifact', type=Path, required=True)
parser.add_argument('--run', required=True)
parser.add_argument('--commit', required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--version', default='0.6.0')
parser.add_argument('--remote-engine', action='store_true')
parser.add_argument('--installer-script-commit', help='Use the installer script from this verified Git commit, keeping the original MSI.')
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
if args.installer_script_commit and not re.fullmatch('[a-f0-9]{40}', args.installer_script_commit):
    parser.error('Commit script non valido')
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
if args.installer_script_commit:
    script = subprocess.run(['git', 'show', args.installer_script_commit + ':installer/Install-OmniDeskAgent.ps1'],
                            check=True, capture_output=True).stdout
    if not script.startswith(b'#Requires -Version 5.1') or len(script) > 65536:
        parser.error('Script installer non valido')
    files['Install-OmniDeskAgent.ps1'] = script
ca = args.ca.read_text()
if 'PRIVATE KEY' in ca or ca.count('BEGIN CERTIFICATE') != 1:
    parser.error('Attesa solo la CA pubblica')
fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(ca)).hexdigest().upper()
if fingerprint != '45926C7ACA3D49E6619E377AD0FA2110951F6F0E54DF9B83A6C6E3AD712BE5B2':
    parser.error('CA diversa da quella LAN concordata; verificarla prima di cambiare il pin')
files['omnidesk-local-ca.crt'] = ca.encode()
files['Installa-OmniDesk.ps1'] = f'''#Requires -Version 5.1
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Install-OmniDeskAgent.ps1') -MsiPath (Join-Path $PSScriptRoot 'OmniDesk.Agent.msi') -ServerUrl https://192.168.1.233:8085 -CaCertificatePath (Join-Path $PSScriptRoot 'omnidesk-local-ca.crt') -CaSha256 {fingerprint} -TrustServerCa -AllowUnsignedForTesting -MsiSha256 {expected['OmniDesk.Agent.msi']}
'''.encode()
files['LEGGIMI.txt'] = f'''OmniDesk Agent + Tray - collaudo LAN x64
Origine: https://github.com/ico88/OmniDesk/actions/runs/{args.run}
Commit MSI: {args.commit}
Commit script: {args.installer_script_commit or args.commit}

Pacchetto unsigned per laboratorio. I checksum non sostituiscono una firma.
Non include credenziali, token o chiavi private.
{remote_note}

1. Estrai il kit su Windows. Verifica SHA256SUMS.txt contro l'artifact GitHub.
2. Dalla console https://192.168.1.233:8085, Dispositivi > Registra un
   dispositivo, genera un token enrollment. Non salvarlo in un file.
3. Da PowerShell amministrativa nella directory estratta esegui:

.\\Installa-OmniDesk.ps1

   Quando richiesto, incolla il token e premi Invio. L'inserimento e mascherato.
   Il token non viene passato nella riga di comando. Il token temporaneo viene
   eliminato al termine; la copia MSI privata resta disponibile per riparazioni. Se gli script sono bloccati, per questa sola finestra:

Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned
Unblock-File -LiteralPath .\\Installa-OmniDesk.ps1
Unblock-File -LiteralPath .\\Install-OmniDeskAgent.ps1

   Poi ripeti .\\Installa-OmniDesk.ps1. Le policy aziendali restano vincolanti.
4. Esegui logoff/logon per avviare la Tray, oppure avviala dalla sessione standard
   senza elevazione. Per automazioni lo script Install-OmniDeskAgent.ps1 mantiene
   l'opzione -EnrollmentTokenFile, da usare con un file protetto e da eliminare.
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
if args.installer_script_commit:
    release['installerScriptCommit'] = args.installer_script_commit
metadata = args.output.parent / ('windows-release.' + uuid.uuid4().hex + '.tmp')
metadata.write_text(json.dumps(release, indent=2) + '\n')
metadata.replace(args.output.parent / 'windows-release.json')
print(f'Kit creato: {args.output}')
print('SHA256 ZIP:', hashlib.sha256(args.output.read_bytes()).hexdigest())
