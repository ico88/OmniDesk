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
| GET /api/branding | autenticato; name,logoUrl,primaryColor,theme |
| PUT /api/branding | administration globale + CSRF; medesimi campi; logo URL HTTPS o vuoto, tema light/dark |
| GET /api/admin/users | administration globale; id,email,roles |
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
