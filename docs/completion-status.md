# Stato consegna Windows/LAN

Questo documento distingue il risultato disponibile dai gate ancora aperti; la roadmap mantiene lo storico degli incrementi.

| Parte | Implementazione | Verifica disponibile | Gate rimanente |
| --- | --- | --- | --- |
| API/dashboard e identita dispositivi | Disponibili su LAN con migrazioni | PostgreSQL, Chromium, build, readiness HTTPS e anonymous401 | Collaudo operatore e produzione |
| Ticket/screenshot | Backend, console IT, servizio broker e Tray | Test isolamento/permessi/consenso/PNG/versione/retry e browser ticket passati | Cattura/anteprima e IPC nella sessione Windows reale |
| Agent + Tray | Componenti distinti, ACL/DPAPI, recovery e MSI unico; candidata 0.8.4 include diagnostica e controllo servizi RMM | Linux run 39244309579 riuscito; Windows 0.8.4 in corso | MSI 0.8.4, installazione sul PC pilota e verifica reale processi/servizi/eventi/azioni |
| Installer e download web | Setup/MSI unico unsigned, CA/enrollment e download amministrativo nella UI; candidata 0.8.4 | Workflow Windows MSI 0.8.4 in corso; package Agent 0.8.3 già passato | Pubblicazione kit e manifesto sul server, firma produzione e prova sul PC destinatario |
| Desktop/terminale/file remoti | Motore Windows incluso, provisioning autorizzato, mapping, storico, lease e revoca persistente | Motore LAN reale: TLS, autenticazione, provisioning idempotente e rimozione gruppo; Windows CI: avvio nativo e arresto senza enrollment | Consenso/multi-sessione, desktop/terminale/file e revoca tunnel su PC reale; LAN/NAT |

**Desktop presidiato LAN confermato dall’operatore dopo autorizzazione manuale del rilevamento Defender sul PC pilota.** Il kit 0.7.2 scaricabile dalla UI include il motore; la configurazione viene autorizzata dal server dopo enrollment. Il PC concordato per la prova e Windows 10, 192.168.1.160. La dipendenza node-forge del motore mantiene inoltre un advisory senza versione corretta: niente rilascio pubblico del motore finche il gate non viene risolto. L’eccezione laboratorio non risolve il gate distribuzione. Rilevamento confermato: HackTool:Win32/Meshagent.MBZ!MTB; dossier in [remote-engine-defender-review](remote-engine-defender-review.md). Non e stato accertato un falso positivo. La conferma operatore non chiude le prove di terminale/file, revoca e multi-sessione. Accesso non presidiato implementato con policy per dispositivo e test autorizzazione/revoca; resta da provare senza logon. UI desktop/mobile riordinata e pubblicata. Agent0.7.2 corregge il lancio PowerShell Update: nuova scansione reale da provare.

Ticket/screenshot costituiscono un incremento minimo. Il prerequisito core SD0 per sedi e permessi per sede è implementato e verificato nei test, ma non ancora distribuito sul server live; outbox, SLA, email, knowledge base, allegati generici e automazioni Service Desk restano fuori da questa consegna. Il backup include DB, Identity, Caddy e dati Mesh; la copia Mesh a caldo non dimostra un restore consistente. Restore completo, retention, firma e altre verifiche di produzione restano gate distinti.

Risultati e run effettivi in [validation](validation.md), procedura client in [windows-lan-install](windows-lan-install.md).
