# Verifica delle immagini compilate su f1

La build API/dashboard è stata confermata dall'operatore. Per eseguire le verifiche dal checkout sul server Ubuntu, con Docker accessibile:

```bash
bash scripts/validate-release.sh
```

La procedura usa le immagini locali esistenti. Esegue prima i test PostgreSQL/API e dashboard, poi avvia un ambiente smoke isolato con migrazioni, bootstrap e Chromium. Infine verifica l'istanza LAN esistente tramite la CA pubblica `omnidesk-local-ca.crt`: readiness, dashboard e asset, accesso anonimo rifiutato. Non installa dipendenze di sistema e non aggiorna il deployment LAN. I test nei container possono scaricare pacchetti. Un fallimento interrompe la procedura; nessun risultato precedente implica il superamento delle fasi successive.

Per eseguire solo la sonda LAN:

```bash
python3 scripts/verify-lan.py --server https://192.168.1.233:8085 --ca omnidesk-local-ca.crt
```

Questa sonda non verifica login, remoto, ticket, screenshot, servizio Windows o MSI. I container running da soli non attestano disponibilità HTTP né utilizzo delle immagini appena compilate. Lo smoke delle nuove immagini e la sonda dell'istanza LAN sono risultati distinti.

## Installer su GitHub Actions

Il workflow locale `.github/workflows/windows.yml` usa `windows-latest` per contratti Tray, parsing PowerShell, build Agent/Tray e MSI unico. La Console WebView2 ha un job separato. Gli artefatti `omnidesk-agent-unsigned` includono MSI, script LAN e checksum; sono pacchetti di laboratorio non firmati. Il packaging non certifica installazione o funzionalità remote.

Il runner usa esclusivamente sorgenti presenti nel commit remoto selezionato: le modifiche non committate di questo checkout non vengono inviate da un workflow dispatch. Prima di avviarlo occorrono pubblicazione autorizzata delle modifiche su un branch e accesso GitHub funzionante. Dopo la pubblicazione, avviare il workflow `windows-agent-package` sul branch corrispondente e verificare esito dei job e artefatti. Non riutilizzare come 0.6.0 un artifact generato da un commit precedente.

Stato e limiti effettivi sono registrati in [validation](validation.md); il codice per ticket/screenshot e il provisioning remoto completo restano da sviluppare.
