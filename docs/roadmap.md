# Roadmap

- M1: infrastruttura Compose/Caddy/PostgreSQL, setup Ubuntu, schema e migrazioni Identity, bootstrap, autenticazione e CSRF, utenti, permessi globali/gruppo/device, audit, dashboard e branding. Consegna iniziale: vedi validation.md per prove effettive e limiti.
- M2 (implementazione iniziale compilata; MSI compilato in CI; gate installazione Windows/firma ancora aperto): servizio Windows .NET con enrollment monouso, chiavi dispositivo, revoca, retry/backoff, metriche CPU/RAM/disco/rete/uptime, inventario; MSI unico firmato e aggiornamento sicuro. Test Windows e reti NAT differenti obbligatori.
- M3 (adapter compilato e permessi testati con fake; gate motore/Windows e audit node-forge aperti): MeshCentral, adapter server con mapping identità e gruppi, sessioni autorizzate e revocabili per remote/terminal/files; MSI coordina i due servizi. Test anti bypass dei permessi e WebSocket/TLS. MeshCentral non viene esposto da M1.
- M4 (coda/API testate, worker cross-compilato; collaudo WUA Windows ancora aperto): Windows Update scan/install, job firmati/idempotenti, reboot policy, scheduling e report.
- M5 (console cross-compilata, TOTP e layout testati; flag TOTP inizialmente false): console Windows WebView2 della stessa UI, dashboard layout per utente, TOTP setup/challenge/recovery/cifratura implementati e testati; hardening, retention, restore, upgrade e release firmata ancora aperti.

Gate produzione: revisione minacce, pen test autorizzazione/CSRF/enrollment, build riproducibili e SBOM, backup ripristinato, certificati pubblici verificati, test MSI install/upgrade/uninstall e rotazione credenziali. Nessuna data di completamento promessa per moduli non implementati.
