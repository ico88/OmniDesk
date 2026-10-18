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

## Percorso futuro Windows Agent e Tray

**Solo pianificato.** Non modifica M1–M5 né avvia lo sviluppo Service Desk. [Specifica tecnica](windows-agent-tray.md). Il MSI attuale resta Agent-only, unsigned; la Tray è distinta dalla console IT WebView2.

| Fase | Consegna proposta | Dipendenze e criteri di accettazione |
| --- | --- | --- |
| WT0 — contratti e autorizzazione | Threat model IPC, matrice privilegi, identità utente/device, flusso consenso remoto e dismissione | Baseline Agent/Identity/RMM; stabilire versioni Windows supportate, service SID, payload e migrazioni. Nessuna fiducia nel solo SID dichiarato o ticket visibile |
| WT1 — lifecycle servizio | ACL servizio/binari/credenziali, Service Recovery, heartbeat di salute e audit anomalie; revoca e procedura uninstall elevata | WT0 e gate install/upgrade M2. Test account standard, crash/riavvio, no login, offline e manutenzione. Allarmi server durevoli dipendono dal motore futuro SD4 |
| WT2 — Tray e pipe | UI non elevata per logon, stato reale connessione, named pipe locale autenticata e metodi limitati; MSI unico Agent+Tray | WT0–WT1; test pipe squatting, ACL, input ostile, sessioni RDP, logoff e Tray chiusa con servizio attivo; firma MSI/binari e install/repair/upgrade/rollback/uninstall |
| WT3 — assistenza e screenshot | Apertura ticket dal device, elenco ticket propri, pairing/login utente e allegati con anteprima/consenso | WT2 e SD0–SD3 per sedi, ticket, portale, intake macchina e allegati. Prima di tali dipendenze non dichiarare operativo il pulsante. Nessuna cattura background o cross-sessione |
| WT4 — consenso remoto end-to-end | Grant breve per device/capacità/logon, indicatore/ritiro consenso, chiusura tunnel e policy unattended separata disabilitata di default | WT2 e gate motore reale M3; WT3 solo per ingresso dal ticket. Test revoca live, permesso perso, timeout/Tray assente, multi-sessione e reti NAT; nessuna escalation privilegi tramite IPC |

WT3 e WT4 hanno dipendenze diverse: assistenza remota dalla console non deve attendere l'intero Service Desk; ticket e screenshot richiedono invece i servizi pertinenti. Nessuna nuova attestazione di protezione anti-admin o collaudo Windows deriva dall'aggiornamento documentale.
