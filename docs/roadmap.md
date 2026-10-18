# Roadmap

- M1: infrastruttura Compose/Caddy/PostgreSQL, setup Ubuntu, schema e migrazioni Identity, bootstrap, autenticazione e CSRF, utenti, permessi globali/gruppo/device, audit, dashboard e branding. Consegna iniziale: vedi validation.md per prove effettive e limiti.
- M2 (implementazione iniziale compilata; MSI compilato in CI; gate installazione Windows/firma ancora aperto): servizio Windows .NET con enrollment monouso, chiavi dispositivo, revoca, retry/backoff, metriche CPU/RAM/disco/rete/uptime, inventario; MSI unico firmato e aggiornamento sicuro. Test Windows e reti NAT differenti obbligatori.
- M3 (adapter compilato e permessi testati con fake; gate motore/Windows e audit node-forge aperti): MeshCentral, adapter server con mapping identità e gruppi, sessioni autorizzate e revocabili per remote/terminal/files; MSI coordina i due servizi. Test anti bypass dei permessi e WebSocket/TLS. MeshCentral non viene esposto da M1.
- M4 (coda/API testate, worker cross-compilato; collaudo WUA Windows ancora aperto): Windows Update scan/install, job firmati/idempotenti, reboot policy, scheduling e report.
- M5 (console cross-compilata, TOTP e layout testati; flag TOTP inizialmente false): console Windows WebView2 della stessa UI, dashboard layout per utente, TOTP setup/challenge/recovery/cifratura implementati e testati; hardening, retention, restore, upgrade e release firmata ancora aperti.

Gate produzione: revisione minacce, pen test autorizzazione/CSRF/enrollment, build riproducibili e SBOM, backup ripristinato, certificati pubblici verificati, test MSI install/upgrade/uninstall e rotazione credenziali. Nessuna data di completamento promessa per moduli non implementati.

## Percorso futuro OmniDesk Service Desk

**Tutte le fasi SD sono proposte, non implementate.** Percorso aggiuntivo: non sostituisce M1–M5 né chiude i gate di produzione. Specifiche in [Service Desk](service-desk.md) e [schema dati](service-desk-data-model.md). Prima di sviluppare saranno stabiliti contratti, matrice permessi e criteri di accettazione per ciascuna fase.

| Fase | Risultato previsto | Dipendenze e gate di ingresso/uscita |
| --- | --- | --- |
| SD0 — basi comuni e progetto esecutivo | Registro sedi, associazione dispositivi e backfill; concessioni per sede; contratti eventi/outbox e audit; decisioni storage, antivirus, retention e identità portale | Inventario e Identity esistenti; nessun gruppo trasformato implicitamente in sede. Verificare migrazione dati e isolamento autorizzativo prima del primo ticket |
| SD1 — ticket e console IT | Modulo separato, migrazioni proprie, ticket manuali, stati/priorità/categorie, code/assegnazioni, uno o più dispositivi e una sede, cronologia/audit; link contestuali alle azioni RMM | SD0; transazioni/ETag/idempotenza e permessi granulari testati. Le azioni RMM richiedono anche capacità correnti e collaudo del relativo motore; non aggirano gate M3/M4 |
| SD2 — portale e collaborazione | Richieste proprie, commenti pubblici/note interne, allegati in quarantena, notifiche e KB iniziale con pubblicazione controllata | SD1; verifica identità utente/associazione dispositivi, storage privato, antivirus e worker. Gate contro fughe via email, ricerca, allegati e conteggi |
| SD3 — ingressi agente/email e storico interventi | Intake macchina autenticati, mailbox/thread verificati, deduplicazione e qualificazione; correlazione sessioni/job e riepilogo interventi | SD1–SD2; protocollo agente esistente, account verificati, gestione segreti mailbox e test spoof/replay/loop. Nessuna identità umana derivata dal solo mittente email |
| SD4 — allarmi, SLA ed escalation | Motore allarmi core persistente, ticket da occorrenze correlate, SLA/calendari versionati, timer durevoli e automazioni allowlist | SD0–SD3 e sviluppo/validazione del motore allarmi, oggi assente. Gate DST/pausa/riapertura/crash, deduplica escalation e prevenzione loop. Nessuna shell automatica |
| SD5 — conoscenza avanzata e AI opzionale | Ricerca autorizzata, KB e storico avanzati; riepiloghi e bozze AI opt-in, eventuali approvazioni umane specifiche per future azioni | SD1–SD4 stabilizzati, policy dati/provider/budget e valutazioni prompt injection. AI senza credenziali o accesso diretto ai comandi; produzione subordinata a restore, carico e sicurezza |

Le fasi non hanno date promesse. KB di base e storico non dipendono dall'AI. L'attivazione email, antivirus o provider AI richiederà configurazione operativa separata: nessun servizio esterno è stato collegato da questa documentazione. I test indicati nelle specifiche sono criteri futuri, non risultati già ottenuti.
