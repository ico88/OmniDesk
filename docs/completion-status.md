# Stato consegna Windows/LAN

Questo documento distingue il risultato disponibile dai gate ancora aperti; la roadmap mantiene lo storico degli incrementi.

| Parte | Implementazione | Verifica disponibile | Gate rimanente |
| --- | --- | --- | --- |
| API/dashboard e identita dispositivi | Disponibili su LAN con migrazioni | PostgreSQL, Chromium, build, readiness HTTPS e anonymous401 | Collaudo operatore e produzione |
| Ticket/screenshot | Backend, console IT, broker e Tray; priorità e assegnazione con controlli di ambito | 62 test xUnit/PostgreSQL e 4 Vitest; build e test isolamento/consenso precedenti | Migrazione su DB LAN popolato, UI/IPC nella sessione Windows reale; code/SLA/KB restano roadmap |
| Agent + Tray | Componenti distinti, ACL/DPAPI, recovery e MSI unico; candidata 0.8.5 aggiunge terminazione processi autorizzata | Suite Linux API/UI in verifica; CI Windows 0.8.5 da avviare | MSI 0.8.5, self-test Windows e prova reale su dispositivo |
| Installer e download web | Setup/MSI unico unsigned, CA/enrollment e download amministrativo nella UI; candidata 0.8.5 | Workflow MSI 0.8.4 ha superato build/install/recovery/uninstall; upgrade/repair/rollback ancora da verificare | Workflow MSI 0.8.5, pubblicazione kit e manifesto sul server, firma produzione e prova sul PC destinatario |
| Desktop/terminale/file remoti | Motore Windows incluso, provisioning autorizzato, mapping, storico, lease e revoca persistente | Motore LAN reale: TLS, autenticazione, provisioning idempotente e rimozione gruppo; Windows CI: avvio nativo e arresto senza enrollment | Consenso/multi-sessione, desktop/terminale/file e revoca tunnel su PC reale; LAN/NAT |

**Desktop presidiato LAN confermato dall’operatore dopo autorizzazione manuale del rilevamento Defender sul PC pilota.** Il kit 0.7.2 scaricabile dalla UI include il motore; la configurazione viene autorizzata dal server dopo enrollment. Il PC concordato per la prova e Windows 10, 192.168.1.160. La dipendenza node-forge del motore mantiene inoltre un advisory senza versione corretta: niente rilascio pubblico del motore finche il gate non viene risolto. L’eccezione laboratorio non risolve il gate distribuzione. Rilevamento confermato: HackTool:Win32/Meshagent.MBZ!MTB; dossier in [remote-engine-defender-review](remote-engine-defender-review.md). Non e stato accertato un falso positivo. La conferma operatore non chiude le prove di terminale/file, revoca e multi-sessione. Accesso non presidiato implementato con policy per dispositivo e test autorizzazione/revoca; resta da provare senza logon. UI desktop/mobile riordinata e pubblicata. Agent0.7.2 corregge il lancio PowerShell Update: nuova scansione reale da provare.

Ticket/screenshot e assegnazione/priorità sono incrementi implementati nel branch, non ancora distribuiti sul server live. La migrazione è stata esercitata nei test isolati; upgrade del DB LAN popolato resta da eseguire. Outbox consumer, code, SLA, email, knowledge base, allegati generici e automazioni restano roadmap. Backup/restore completo, retention, firma e altre verifiche di produzione restano gate distinti.

Risultati e run effettivi in [validation](validation.md), procedura client in [windows-lan-install](windows-lan-install.md).
