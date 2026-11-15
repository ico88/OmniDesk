# Recon map: TeamViewer Remote — connessione remota (Windows, console web/desktop)

Scope: solo la **connessione remota** di TeamViewer Remote: avvio sessione
(assistita con codice/link, ID+password, non assistita da elenco dispositivi),
consenso, toolbar in sessione, trasferimento file, chiusura, sicurezza di
accesso, registrazione e report connessioni. Esclusi Tensor, Frontline, IoT,
Assist AR, DEX, Remote Management (patch/asset/monitoraggio: già coperti da
OmniDesk), meeting.
For: uso interno (IT cricatania). Obiettivo: sostituire in OmniDesk l'attuale
remoto via client RustDesk esterno / adapter MeshCentral con una funzione di
controllo remoto integrata, di livello TeamViewer, gestita da OmniDesk.
Date: 2026-11-15

Metodo: solo fonti pubbliche (knowledge base e pagine marketing TeamViewer).
Nessun account TeamViewer usato, nessun binario, bundle JS o traffico di rete
analizzato. **Le schermate sotto sono ricostruite dal testo della KB, non da
screenshot**: layout e stati non descritti nella KB sono segnati `non
documentato`. `replica/screens/` è vuota.

## Sources

| # | source | URL | notes |
| --- | --- | --- | --- |
| 1 | marketing site (IT) | https://www.teamviewer.com/it/ | posizionamento Remote / Tensor / ONE; WoL, screen sharing |
| 2 | pricing | https://www.teamviewer.com/it/pricing/ | schede piani non leggibili in modo statico; FAQ: licenza solo all'esperto, canali = sessioni concorrenti |
| 3 | KB toolbar sessione | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/remote-session-toolbar/ | elenco completo menu Azioni / Vista / Comunica / File ed extra, differenze macOS |
| 4 | KB supporto assistito | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/provide-attended-remote-support/ | sessione con nome, link/codice/email, sala d'attesa, QuickSupport, consenso |
| 5 | KB supporto non assistito | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/provide-unattended-remote-support/ | Host, "Manage this device", connessione da elenco senza conferma |
| 6 | KB ID e password | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/connect-via-id-and-password/ | ID+password come ripiego; link di sessione raccomandati |
| 7 | KB dove trovare ID/password | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/where-to-find-my-id-and-password/ | posizione in client, QuickSupport, Host |
| 8 | KB report connessioni | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/security/connection-reports/ | report in uscita/in entrata, filtri, CSV in UTC |
| 9 | KB policy | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/devices/policies/ | assegnazione a device/gruppo, ereditarietà esplicita, enforce |
| 10 | KB policy registrazione | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/devices/session-recording-policies/ | registrazione automatica in entrata/uscita, conferma supporter |
| 11 | KB block/allowlist | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/security/block-and-allowlist/ | deny/allow per ID, account, azienda |
| 12 | KB dispositivi attendibili | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/security/trust-devices-via-push-notification/ | approvazione login via push |
| 13 | KB Wake-on-LAN | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/remote-control/wake-up-a-device-remotely/ | via peer in LAN o indirizzo pubblico |
| 14 | KB security statement | https://www.teamviewer.com/en/global/support/knowledge-base/teamviewer-remote/security/security-statement/ | RSA 4096 / AES-256, mTLS 1.3 dalla 15.73, E2E, backoff tentativi, password 6 caratteri |

Fonti non usate per ora: video walkthrough pubblici, app store listing,
API pubbliche (https://www.teamviewer.com/it/global/support/for-developers/).

## Core loop

Il tecnico apre il dispositivo (o crea una sessione con codice), l'utente
acconsente quando richiesto, il tecnico controlla lo schermo con gli strumenti
della toolbar, chiude, e la connessione resta tracciata nel report.

## Screens

| ID | screen | route / how to reach | purpose | key components | states seen |
| --- | --- | --- | --- | --- | --- |
| S01 | Remote Support (tecnico) | menu laterale "Remote Support" (client o web) | punto di partenza: nuova sessione, campo ID | lista sessioni, "New session", campo ID + Connect | filled; vuoto non documentato |
| S02 | Nuova sessione | S01 → New session | nominare la sessione e condividere l'invito | input nome, copia link, copia codice, invito email, Start / "Create for later" | filled |
| S03 | Sala d'attesa | S02 → Start | aspettare che il cliente entri | stato attesa, popup "cliente arrivato", Join session | waiting, cliente arrivato; scadenza non documentata |
| S04 | Pagina invito (cliente, browser) | link sessione | scaricare il modulo di supporto | Join session, download per Windows/macOS | filled |
| S05 | Modulo di supporto temporaneo (cliente) | eseguibile scaricato da S04 | mostrare ID/password, accettare condizioni, avvisare che il tecnico attende | ID, password, accetta termini, Continue | waiting |
| S06 | Prompt consenso (cliente) | il tecnico fa Join | accettare o rifiutare l'accesso | Allow access, Decline | prompt, rifiutato (esito non documentato) |
| S07 | Inserimento password (tecnico) | S01 → ID → Connect | autenticarsi al dispositivo con password | campo password, Connect | errore password: non documentato |
| S08 | Elenco dispositivi | menu Devices | connettersi a dispositivi gestiti senza consenso | gruppi, ricerca, riga dispositivo, Connect, stato online | online/offline (inferito) |
| S09 | Finestra sessione + toolbar | dopo S06 / S07 / S08 | controllo remoto | toolbar Azioni / Vista / Comunica / File ed extra, quick actions, tab sessioni, X | in sessione, schermo nero attivo, input remoto disabilitato |
| S10 | Trasferimento file | S09 → File ed extra → File transfer | copiare file nei due sensi | due pannelli file, coda trasferimenti | dettaglio non letto |
| S11 | File box | S09 → File ed extra → File box | condividere file rapidamente in sessione | area drop | dettaglio non letto |
| S12 | Chat | S09 → Comunica → Chat | messaggi testuali tra tecnico e utente | lista messaggi, input | dettaglio non letto |
| S13 | Informazioni | S09 → Azioni → Information | info sistema remoto, connessione, sessione | tre pannelli | dettaglio non letto |
| S14 | Nota lasciata (cliente) | S09 → Azioni → Leave note | messaggio che resta visibile al cliente dopo la chiusura | finestra nota | visibile dopo fine sessione |
| S15 | Host: gestisci dispositivo | client Host / Impostazioni → Generale | assegnare il PC all'account per l'accesso non assistito | Manage this device, login | non assegnato, assegnato |
| S16 | Impostazioni sicurezza: block/allowlist | Settings → Security | limitare chi può connettersi | radio deny/allow, lista partner, Add/Remove, "anche per meeting" | vuota, filled |
| S17 | Policy dispositivo | Company → Policies | impostare regole su device/gruppi | elenco policy, Assign, regole con enforce | dettaglio regole non letto |
| S18 | Report connessioni | Admin settings → Connection reports | storico sessioni in uscita/in entrata | tabella, filtri, stampa, CSV | filled; vuoto non documentato |
| S19 | Wake-on-LAN | Settings → Device → Network → Wake-on-LAN; azione dal device | accendere il PC prima di connettersi | metodo (peer LAN / indirizzo pubblico), ID peer, host+porta UDP | configurato / no |
| S20 | Approvazione dispositivo attendibile | push sul telefono al login | approvare un nuovo dispositivo di login | Trust this device / IP / only once | pending (inferito) |

## Flows

```
F01 Tecnico assiste un utente con sessione a codice/link (assistito)
    S01 -> S02 -> S03 -> (cliente S04 -> S05) -> S03 Join -> (cliente S06 Allow) -> S09
    happy path clicks: tecnico ~5, cliente 3–5 (fonte 4; conteggio stimato)
    edge: cliente rifiuta, cliente non arriva, link scaduto (non documentato),
          cliente senza diritti admin (UAC), più tecnici nella stessa sessione

F02 Tecnico si connette con ID + password
    S01 (ID) -> S07 (password) -> S09
    happy path clicks: 2 + digitazione
    edge: password errata, tentativi ripetuti (backoff, fonte 14), ID offline

F03 Tecnico apre un PC gestito senza presenza utente (non assistito)
    S08 -> Connect -> S09        (nessuna conferma lato remoto, fonte 5)
    happy path clicks: 2
    edge: PC offline (-> F07), utente collegato al PC, policy che richiede conferma,
          sessione su schermata di login/UAC, permessi tecnico revocati

F04 Configurare un PC per l'accesso non assistito
    a) da F01: S09 -> File ed extra -> installa Host -> reconnect
    b) manuale: installer Host -> EULA -> S15 Manage this device -> login
    happy path clicks: ~6 (a), ~5 (b)
    edge: installazione fallita, PC già assegnato ad altro account

F05 Lavorare in sessione
    S09: Ctrl+Alt+Del, riavvio (anche provvisoria) con riconnessione, blocco/logoff,
    appunti, combinazioni tasti, schermo nero, disabilita input, multi-monitor,
    qualità/scala, chat S12, file S10/S11, screenshot, registrazione, nota S14
    edge: riavvio con riconnessione automatica, cambio risoluzione, monitor staccato

F06 Chiudere la sessione
    S09 -> X -> (blocco a fine sessione secondo impostazione Always/Never/Automatic)
    -> voce in S18
    edge: chiusura lato utente, perdita rete, revoca permessi a sessione aperta

F07 Accendere un PC spento e connettersi
    S08 -> Wake -> attesa online -> F03
    edge: NIC/BIOS non configurati, nessun peer nella LAN

F08 Amministratore controlla chi si è connesso
    S18 -> filtri (utente, device, gruppo, date) -> CSV
```

## Components

| component | variants | states | used on |
| --- | --- | --- | --- |
| Toolbar sessione | menu a tendina, quick actions, collassata/espansa | normale, fullscreen, collassata | S09 |
| Menu toolbar | Azioni, Vista, Comunica, File ed extra | voce attiva/disattiva, richiede licenza (nascondere) | S09 |
| Tab sessione | una per sessione aperta | attiva, in riconnessione | S09 |
| Campo ID/codice | ID dispositivo, codice sessione | vuoto, valido, errore | S01, S05 |
| Card credenziali | ID + password temporanea | visibile, copiata | S05, S15 |
| Dialog consenso | Allow / Decline | in attesa, accettato, rifiutato | S06 |
| Dialog password | — | errore, backoff | S07 |
| Riga dispositivo | online, offline, in sessione | hover con Connect / Wake | S08 |
| Indicatore sessione lato utente | — | tecnico connesso, schermo nero, registrazione attiva | S09 (lato remoto, inferito) |
| Pannello file a due colonne | locale / remoto | in trasferimento, errore | S10 |
| Chat | — | vuota, messaggi | S12 |
| Tabella report | — | vuota, filtrata | S18 |
| Toggle policy | normale / enforced | — | S17 |
| Lista partner | deny, allow | vuota, filled | S16 |
| Toast / notifiche | info, errore | — | ovunque |

## Inferred data model

```
Device        id, name, group_id, online, last_seen, agent_version, os, wol_config
              evidence: S08, S15, S19 · confidence: high (esiste già in OmniDesk)

DeviceAccessCredential  device_id, display_id, temp_password (ruota per avvio), expires_at
              evidence: S05, S07, fonte 7, 14 (password 6 caratteri alfanumerici)
              confidence: medium (rotazione non documentata)

SupportSession  id, name, code, link_token, created_by, state
              (created | waiting | joined | consent_pending | active | declined | ended | expired),
              scheduled ("create for later"), created_at, expires_at
              evidence: S02, S03, S06, fonte 4 · confidence: medium (scadenza: guess)

RemoteSession id, device_id, actor_id, mode (attended | unattended | id_password),
              capability (view | control | files), consent_required, started_at, ended_at,
              end_reason, recording_id
              evidence: S09, S18, fonte 8 · confidence: high
              (OmniDesk ha già RemoteSession con State/ConsentRequired/ExpiresAt/EndedAt)

SessionParticipant  session_id, user_id, role (owner | invited)
              evidence: "Invite more participants" fonte 3 · confidence: medium

SessionEvent  session_id, at, type (chat | file_transfer | reboot | black_screen | clipboard ...),
              detail (senza contenuti sensibili)
              evidence: toolbar fonte 3 · confidence: guess (TeamViewer non documenta un log eventi)

Recording     id, session_id, storage_location, started_at, auto (bool), confirmed_by
              evidence: fonte 10 · confidence: high

Policy        id, name, rules[{key, value, enforced}], assigned to device | group,
              device.inherits_from_group_id
              evidence: fonte 9 · confidence: high

AccessListEntry  device_id or company scope, mode (deny | allow), subject (id | account | company)
              evidence: fonte 11 · confidence: high

ConnectionReport  = vista su RemoteSession: user, origin, target device, group, start, end, duration
              evidence: fonte 8 · confidence: high

TrustedLoginDevice  user_id, device_fingerprint | ip, trust (permanent | ip | once), approved_at
              evidence: fonte 12 · confidence: medium
```

Relationships: Group 1-n Device; Device 1-n RemoteSession; SupportSession 0-1
RemoteSession; RemoteSession 1-n SessionParticipant, 1-n SessionEvent, 0-1
Recording; Policy n-n Device/Group; Device 1-n AccessListEntry.

## Cosa ha già OmniDesk (2026-11-15)

- Permesso `remote.control` separato, ambiti global/group/device, audit.
- Entità `RemoteSession` (state, consent, scadenza, fine) e adapter MeshCentral
  con sessioni limitate; non dichiarato operativo in produzione.
- RustDesk: mapping peer/device, avvio del client esterno con audit; OmniDesk
  **non osserva** lo stato della sessione, consenso, disconnessione o revoca.
- API terminale/file nel server, nascoste dalla console.
- Agent Windows con identità dispositivo, enrollment monouso, Tray utente.

Il trasporto remoto (cattura schermo, input, codec, relay/NAT) è la parte che
oggi OmniDesk delega a RustDesk/MeshCentral ed è la parte più costosa da
rifare.

## Feature matrix

See `features.csv`. Must: 21, should: 17, could: 9, skip: 8 (55 righe; una, "collegare la
sessione a un ticket", è un'aggiunta OmniDesk e non entra nel punteggio di parità).

## Out of scope (cannot or should not be cloned)

- Rete globale di router/relay TeamViewer e la sua raggiungibilità ovunque:
  per uso interno serve un relay proprio (self-hosted).
- Tia / AI session summary, Augment session (AR), Assist AR.
- TeamViewer Printing e VPN TeamViewer (driver proprietari).
- Supporto mobile (iOS/Android) e macOS/Linux: OmniDesk è Windows-only.
- Meeting e integrazioni marketplace.
- Codici, link e infrastruttura identitaria TeamViewer (account, aziende).

## Size

Screens 20, flows 8, entities 11. Hard parts:
1. **Trasporto**: cattura desktop Windows (inclusi login, UAC, desktop sicuro
   tramite servizio SYSTEM), codec video, input, multi-monitor, attraversamento
   NAT con relay self-hosted, crittografia E2E con librerie standard.
2. **Viewer**: client tecnico (browser via WebRTC/WebSocket o console WebView2)
   con latenza accettabile.
3. **Riavvio con riconnessione e sicurezza**: consenso, schermo nero, revoca a
   sessione aperta, registrazione.

Size: **XL** se si riscrive il trasporto da zero. Proposta di riscoping:
mantenere un motore open source come trasporto (RustDesk server self-hosted o
MeshCentral, già integrati) e portare in OmniDesk tutto il resto
(orchestrazione sessioni, consenso, policy, toolbar/azioni via Agent, report,
registrazione, WoL). Così diventa **L**. Nessuna promessa di parità con
TeamViewer sulla qualità dello stream.
