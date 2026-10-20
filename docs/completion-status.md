# Stato consegna Windows/LAN

Questo documento distingue il risultato disponibile dai gate ancora aperti; la roadmap mantiene lo storico degli incrementi. Al 2026-10-20 il branch `feature/windows-support-lan` include allegati/quarantena, flusso SLA scoped con pause, cicli di riapertura ed escalation deduplicate, intake email firmato con inbox dashboard e promozione manuale, avvisi al richiedente per le risposte pubbliche, panoramica rete e ricerca Knowledge Base full-text con GIN. La revisione `0d52ba7` è la base distribuita sul server LAN; il codice `160d1a7` è l’ultima revisione applicativa LAN e il DB live resta a 50 migrazioni. Readiness HTTPS/CA, asset dashboard e rifiuto anonimo sono stati verificati. Il kit LAN 0.8.12 è disponibile nel download amministrativo; il bundle 0.8.9 resta conservato per rollback. La selezione rete 0.8.12 invia solo connessioni `Up` con IP unicast e gateway, ma il PC pilota deve ancora installare l’aggiornamento e inviare un nuovo heartbeat per sostituire le metriche precedenti. Il branch `137e7d7` passa suite API 97/97, Vitest 6/6, smoke Chromium/restore isolato, CI Linux `39397632930` e Windows `39397632990`. Restano il download autenticato e l’aggiornamento interattivo sul PC Windows pilota, provider email reale, account portale cliente e ciclo completo reply/thread.

Il branch contiene Agent/Tray/Setup/MSI 0.8.10 con visualizzazione nella Tray
degli allegati pubblici collegati ai messaggi, dopo scansione antivirus. CI
Linux `39385189484` e Windows `39385189469` sono verdi e il nuovo artifact MSI
è stato generato e collaudato su runner isolato; non è ancora stato promosso al
download sul server LAN. Il kit disponibile sul server resta 0.8.9.

Aggiornamento branch 2026-10-20: Agent/Tray/Setup/MSI passa a 0.8.11 per
consentire il download esplicito dalla Tray degli allegati pubblici puliti. Il
server rivalida ogni richiesta con la credenziale dispositivo e la chiave
richiedente, e rifiuta allegati infetti o non più visibili. La suite locale
passa 96/96 API/PostgreSQL e 6/6 Vitest con build dashboard. CI Linux
`39390505418` e Windows `39390505409` sono verdi; il pacchetto unsigned 0.8.11 è
stato generato ma non pubblicato sulla LAN: il download web sul server resta
0.8.9. Il collaudo interattivo sul client Windows pilota resta aperto.

Verifica CI aggiornata: Linux `39390505418` è passato su 0.8.11, includendo test,
build API/dashboard, Compose e smoke Chromium/restore. Windows `39390505409` ha
superato build MSI, install/recovery/uninstall e upgrade/repair/rollback sulle
baseline 0.6.0 e 0.7.0, eseguite in job separati. Download Tray e flusso
end-to-end restano da provare interattivamente sul PC Windows pilota.

Il workflow Windows è stato aggiornato nel commit `1bf8ee7`: pubblica il
candidate MSI dopo il lifecycle test e divide le verifiche sulle baseline 0.6.0
e 0.7.0 in due job isolati. La nuova struttura è stata verificata dalla CI
`39390505409`; il precedente run `39386842846` resta attivo e non è stato
interrotto. GitHub ora riporta quel run come `cancelled`; nessun comando di
cancellazione è stato inviato in questo incremento. La disponibilità
dell’artifact non equivale alla promozione del pacchetto LAN.

Aggiornamento backup 2026-10-20: il branch include retention locale opt-in
`OMNIDESK_BACKUP_RETENTION_DAYS` (default `0`), che elimina solo backup diretti
verificati e scaduti dopo il completamento di un nuovo backup. Test shell,
suite applicativa, smoke/restore e CI Linux `39392940229` passano; CI Windows
`39392940145` completa la matrice MSI. Nessuna retention è stata attivata sui
backup live; cifratura e copia off-site restano aperte.

Hardening aggiunto: la validazione della soglia ora precede
qualsiasi comando Docker e la directory `.backups` viene rifiutata se file o
symlink. Test mirati e configurazione Compose passano localmente; la CI di
queste modifiche è passata: Linux `39394376992`, Windows `39394376714`.

| Parte | Implementazione | Verifica disponibile | Gate rimanente |
| --- | --- | --- | --- |
| Presenza e audit Agent | Online/Offline da heartbeat firmato; baseline persistente e audit deduplicato delle transizioni, revocati esclusi, soglia/poll configurabili; campanella e inbox notificano solo dispositivi visibili; email offline opzionale con consenso e rivalidazione scope; retention opt-in configurabile per audit, metriche e inventario (default disattivata) | Test integrato PostgreSQL controlla isolamento device e consegna SMTP fake; suite 95/95, dashboard 6/6 Vitest, smoke Chromium/restore e build Docker passano localmente | Revisione non pubblicata sulla LAN; SMTP reale, altri canali esterni, policy normative prima di abilitare retention in produzione restano aperte |
| API/dashboard e identita dispositivi | Disponibili su LAN con migrazioni fino alla 50; elenco dispositivi con badge Online/Offline/Revocato da heartbeat; inbox email firmata con promozione manuale e retention intake; panoramica rete aggiornata su ogni heartbeat, anche prima dell’inventario. La ricerca KB nel branch usa `tsvector` generato e GIN | Suite API/PostgreSQL 97/97, build dashboard e 6/6 Vitest; smoke Chromium/restore isolato con 53 migrazioni. Dopo deploy `160d1a7`: readiness HTTPS/CA, asset dashboard e deny anonimo passati; 0 sessioni remote e 0 job aggiornamento | Ricerca KB e migrazione full-text non distribuite sulla LAN; collaudo autenticato da account cliente/operatore, provider email reale e gate di produzione |
| Ticket/screenshot/KB/allegati | Backend e dashboard IT LAN; nel branch e sulla revisione LAN `0d52ba7`: workflow versionati, correlazione RMM, riepiloghi append-only, collaboratori scoped, portale personale con ticket/chat/screenshot, allegati generici, intake email firmato/inbox/promozione manuale, refresh automatico ogni 30 secondi. Risposte pubbliche agli operatori generano una notifica scoped al richiedente; click e deep link aprono il dettaglio nel portale, con accesso revocato rivalidato. Allegati privati con quote, consenso pubblico, visibilità interna, ClamAV, blocco download pre-scan, retry autorizzato, notifiche post-scan e backup blob con SHA-256; il backup marca `READY` dopo verifica DB/blob e restore isolato. SLA scoped/versionati: timer, completamento, breach audit/outbox, policy/calendari, pausa e ripresa con deadline recuperata, nuovo ciclo resolution alla riapertura, soglie escalation persistenti e notifica scoped deduplicate, consegne email in coda con consenso e permesso validati. Retention intake (30/180 giorni), allegati, contenuto ticket risolto, audit, metriche e inventario (policy opt-in disattivate per default); il contenuto redatto mantiene numero, stato e audit operativo. Il branch aggiunge il vincolo PostgreSQL differibile che impedisce ticket senza device associati e ricerca full-text KB con GIN | `./scripts/test.sh`: 97 API/PostgreSQL, build dashboard e 6/6 Vitest; build Docker API, Compose config, smoke Chromium multi-termine KB e restore isolato; CI Linux `39382194435` e Windows `39382194419` verdi per una revisione precedente | Le migrazioni di integrità e ricerca full-text non sono ancora pubblicate sulla LAN. Restano prova portale con account reale, provider/thread/reply email, restore periodico off-site e retention backup. SMTP reale, allarmi, automazioni e test runtime SLA approfondito restano aperti |
| Agent + Tray | Componenti distinti, ACL/DPAPI, recovery e MSI unico. Il branch 0.8.12 limita la raccolta alle interfacce `Up` con IP unicast e gateway; il kit LAN 0.8.12 è stato pubblicato | CI Windows `39397632990` verde: build MSI, install/recovery/uninstall e upgrade/repair/rollback da 0.6.0 e 0.7.0; CI Linux `39397632930`; kit 0.8.12 pubblicato e hash verificato nel container API | Prova interattiva Tray/IPC, rete filtrata e ticket sul dispositivo Windows reale; firma Authenticode e gate Defender restano aperti |
| Installer e download web | Workflow Windows genera MSI e console; kit LAN 0.8.12 disponibile dalla UI autenticata per amministratori; bundle 0.8.9 conservato per rollback | Hash e dimensione del bundle verificati anche dal container API; endpoint download anonimo risponde 401; CI Windows `39397632990` verde | Download autenticato e aggiornamento sul PC pilota ancora da provare; firma produzione e gate Defender restano aperti |
| Desktop/terminale/file remoti | Motore Windows incluso, provisioning autorizzato, mapping, storico, lease e revoca persistente; amministratore può disconnettere temporaneamente un operatore lasciando il lease riconnettibile | API/PostgreSQL verifica permessi e sessione ancora attiva; smoke Chromium verifica nome operatore, disconnessione temporanea e messaggio per la riconnessione. Motore LAN reale: TLS, autenticazione, provisioning idempotente e rimozione gruppo; Windows CI verifica avvio nativo/arresto senza enrollment | Sessione Mesh reale per confermare durata 30 minuti, disconnessione temporanea e riconnessione, consenso/multi-sessione, terminale/file e revoca tunnel su PC; LAN/NAT |

**Desktop presidiato LAN confermato dall’operatore dopo autorizzazione manuale del rilevamento Defender sul PC pilota.** Il kit 0.7.2 scaricabile dalla UI include il motore; la configurazione viene autorizzata dal server dopo enrollment. Il PC concordato per la prova e Windows 10, 192.168.1.160. Il gate node-forge resta aperto: npm audit segnala GHSA-86w9-cpqp-85rv su 1.4.0 e non propone una correzione; l’ultima MeshCentral 1.2.5 dipende ancora da 1.4.0 e la PR upstream per la correzione è aperta. Il motore non è quindi ammesso a una release pubblica finché non si adotta e verifica un rilascio corretto. L’eccezione laboratorio non risolve il gate distribuzione. Rilevamento confermato: HackTool:Win32/Meshagent.MBZ!MTB; dossier in [remote-engine-defender-review](remote-engine-defender-review.md). Non è stato accertato un falso positivo. La conferma operatore non chiude le prove di terminale/file, revoca e multi-sessione. Accesso non presidiato implementato con policy per dispositivo e test autorizzazione/revoca; resta da provare senza logon. UI desktop/mobile riordinata e pubblicata. Agent 0.7.2 corregge il lancio PowerShell Update: nuova scansione reale da provare.

Ticket, screenshot, numerazione, categorie, code globali e per sede, membership, routing, assegnazione/priorità e associazioni multi-dispositivo sono distribuiti sul server LAN; il portale personale, gli allegati generici, la KB versionata/scoped, il consumer notifiche e la policy retention outbox sono nel branch ma non in questa installazione LAN. Il vincolo SD1 same-site pubblicato sull'API è stato verificato dopo backup e controllo di sessioni/job; readiness HTTPS/CA e rifiuto anonimo passano. La membership filtra gli assegnatari candidati e viene verificata nelle assegnazioni; non concede accesso ai ticket o ai dispositivi. Il routing sceglie una coda attiva compatibile con la sede per categoria, con override di sede e fallback globale, senza assegnare operatori. La KB nel branch supporta bozze, revisioni consultabili e lettura globale/sede/gruppo; gestione e pubblicazione restano globali e i contenuti sono testuali. Restano gate distinti: provider/thread/reply email, allarmi e notifiche esterni, automazioni, retention ticket/allegati/telemetria, restore periodico LAN/off-site, firma Authenticode e verifiche di produzione, oltre al runtime SLA sulla LAN.

Risultati e run effettivi in [validation](validation.md), procedura client in [windows-lan-install](windows-lan-install.md).

**Ingresso email, non pubblicato sulla LAN:** il branch include webhook normalizzato firmato HMAC-SHA256 (timestamp ±5 minuti, allowlist mailbox, limite payload e deduplicazione), inbox dashboard, discard auditato e promozione manuale con `tickets.manage`. L'operatore sceglie il device e conferma che il mittente non è verificato; il ticket ha origine `email` e `RequesterUserId` nullo. Il webhook non collega Microsoft 365/Google/gateway, non autentica il mittente e non collega thread o reply. Chiave e mailbox devono essere configurate per un provider reale; adapter, thread/reply verificati e prova LAN restano aperti.
