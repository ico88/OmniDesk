# API — nucleo M1 e moduli successivi

JSON, stessa origine, cookie Identity. Errori 400/401/403/409/429; nessun redirect HTML per auth. UUID negli identificatori.

| Metodo/percorso | Accesso e contenuto |
| --- | --- |
| GET /api/health/live | anonimo, processo attivo |
| GET /api/health/ready | anonimo, DB raggiungibile e schema applicato |
| GET /api/auth/csrf | anonimo; {token}, cookie antiforgery; no cache |
| POST /api/auth/login | CSRF; {email,password}; lockout; 204 o 401 |
| POST /api/auth/logout | autenticato, CSRF; 204 |
| GET /api/auth/me | autenticato; id,email, globalPermissions, twoFactorAvailable (flag server), twoFactorEnabled |
| GET /api/devices | autenticato; soli dispositivi con view; lista vuota fino a enrollment M2 |
| PUT /api/admin/devices/{id} | administration globale + CSRF; {name,groupId,expectedName,expectedGroupId}; rinomina/cambio gruppo, 409 se i valori correnti differiscono da quelli attesi |
| POST /api/admin/devices/{id}/revoke | administration globale + CSRF; revoca idempotente, annulla job queued e richiede revoca sessioni remote attive; nessuna disinstallazione o riattivazione implicita |
| GET /api/branding | autenticato; name,logoUrl,primaryColor,theme |
| PUT /api/branding | administration globale + CSRF; medesimi campi; logo URL HTTPS o vuoto, tema light/dark |
| GET /api/admin/users | administration globale; id,email,roles,isDisabled |
| PUT /api/admin/users/{id}/status | administration globale + CSRF; {isDisabled}; 204, 404 o 409; vietata autosospensione |
| PUT /api/admin/users/{id}/roles | administration globale + CSRF; {roles: string[]}; sostituisce i ruoli globali, preserva concessioni locali; ultimo amministratore protetto |
| POST /api/admin/users/{id}/revoke-sessions | administration globale + CSRF; invalida tutte le sessioni web dell'account, inclusa quella corrente se il destinatario è l'operatore |
| GET /api/admin/roles | administration globale; ruoli e permessi |
| POST /api/admin/roles | administration globale + CSRF; {name,permissions[]}; permessi indipendenti, nomi univoci |
| GET /api/admin/grants | administration globale; elenco concessioni |
| POST /api/admin/users | administration globale + CSRF; {email,password,role} nome ruolo esistente oppure null (solo concessioni locali); 201 |
| POST /api/admin/grants | administration globale + CSRF; {userId,role,groupId?,deviceId?}; ruolo esistente, esattamente un ambito; 201 |
| DELETE /api/admin/grants/{id} | administration globale + CSRF; revoca; 204 |
| GET /api/admin/audit | administration globale; ultimi 100 eventi decrescenti |

Permessi distinti: view, remote.control, terminal, files, updates, administration. Endpoint di controllo remoto/terminale/file/update assenti in M1. Grant locale administration non abilita gli endpoint globali. Nessuna registrazione pubblica. Bootstrap amministratore tramite comando dedicato e secret file temporaneo, una sola volta quando non esistono utenti.

M2: enrollment token monouso, heartbeat con identità device separata, gruppi, inventario e metriche. M3: adapter MeshCentral, sessioni effimere dopo autorizzazione, audit ed esplicita revoca; nessuna condivisione credenziali admin MeshCentral con browser. M4: job Windows Update idempotenti con approvazione, stato, timeout e audit. API future non ancora disponibili.

API aggiuntive implementate: vedere [M2 enrollment/metriche/inventario](milestone-2.md), [M3 sessioni MeshCentral](milestone-3.md), [M4 Windows Update](milestone-4.md), [M5 account/TOTP/layout](milestone-5.md). M3 richiede motore configurato e mapping; restituisce 503 se indisponibile, senza simulare sessioni.

Lifecycle account: sospensione, cambio ruoli e revoca sessioni aggiornano il security stamp. Cookie verificato contro DB a ogni richiesta; riattivare un account non ripristina cookie precedenti. Transazioni di stato/ruoli serializzate tramite advisory lock PostgreSQL e nuova verifica del permesso del chiamante; resta almeno un account abilitato con `administration` globale, anche tramite ruolo personalizzato. La revoca sessioni web non equivale alla chiusura remota: il worker remoto rivalida capacità, stato account/device e scadenza; sospensione accoda anche le sessioni remote attive per revoca persistente con retry. Richieste già in esecuzione non vengono retroattivamente cancellate.

Gestione dispositivi: `name` è l'etichetta OmniDesk e non rinomina Windows. `groupId: null` rimuove l'appartenenza al gruppo; le concessioni esplicite sul dispositivo restano valide e quelle di gruppo seguono l'appartenenza corrente. Il cambio gruppo rivalida i job queued e porta le sessioni remote attive a revocation_pending, anche per operatori che conservano capacità globali. Il motore deve ancora confermare la chiusura, con i limiti di M3.

Accodamento, claim, cancellazione queued, modifica metadata e revoca dispositivo usano lo stesso advisory lock transazionale per dispositivo. Il claim ricontrolla la credenziale macchina e il permesso `updates` corrente di RequestedBy: account sospeso, concessione rimossa o cambio ambito impediscono il ritiro e annullano il job queued con audit. Il lock non promette l'annullamento retroattivo di lavori leased/in corso né serializza tutte le modifiche globali ai ruoli; la verifica permessi avviene al momento della decisione di claim. Un risultato già in esecuzione non equivale a una nuova autorizzazione a eseguire comandi.

## Supporto Windows 0.6.0

Contratti ticket/screenshot, identità locale del richiedente, permessi `tickets.view`/`tickets.manage`, limiti e idempotenza: [support-implementation.md](support-implementation.md). `/api/devices/{id}/remote-sessions/availability` espone la presenza di configurazione/mapping, senza attestare connessione reale del dispositivo. NodeId visibile solo ad amministrazione globale.
