# Stato consegna Windows/LAN

Questo documento distingue il risultato disponibile dai gate ancora aperti; la roadmap mantiene lo storico degli incrementi.

| Parte | Implementazione | Verifica disponibile | Gate rimanente |
| --- | --- | --- | --- |
| API/dashboard e identita dispositivi | Disponibili su LAN con migrazioni | PostgreSQL, Chromium, build, readiness HTTPS e anonymous401 | Collaudo operatore e produzione |
| Ticket/screenshot | Backend, console IT, servizio broker e Tray | Test isolamento/permessi/consenso/PNG/versione/retry e browser ticket passati | Cattura/anteprima e IPC nella sessione Windows reale |
| Agent + Tray | Componenti distinti, ACL/DPAPI, recovery e MSI unico | Build Windows CI, self-test Agent, contratti pipe | Sessioni utente/RDP, lifecycle reale su PC destinatario e LAN |
| Installer | MSI unsigned da runner Windows e script CA/enrollment | Artifact Windows disponibile; install/crash/recovery/uninstall sul runner Windows passati | Firma produzione, repair/upgrade/rollback, collaudo destinazione |
| Desktop/terminale/file remoti | Adapter MeshCentral, autorizzazioni separate, mapping, storico e revoca persistente | Contratti e permessi con fake; verifica sorgenti upstream | Provisioning Mesh agent nel pacchetto, motore reale, consenso/multi-sessione, revoca tunnel e LAN/NAT |

**Il desktop remoto completo non e ancora consegnato.** Il pacchetto Agent+Tray non include un MeshCentral Agent configurato. La dipendenza node-forge del motore mantiene inoltre un advisory senza versione corretta: niente rilascio pubblico del motore finche il gate non viene risolto. Nessun test simulato dimostra funzionamento di un desktop Windows.

Ticket/screenshot costituiscono un incremento minimo; sedi, SLA, email, knowledge base, allegati generici e automazioni delle fasi SD non fanno parte di questa consegna. Backup completo/restore chiavi, retention, firma e altre verifiche di produzione restano gate distinti.

Risultati e run effettivi in [validation](validation.md), procedura client in [windows-lan-install](windows-lan-install.md).
