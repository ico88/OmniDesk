# API M1

JSON, stessa origine, cookie Identity. Errori 400/401/403/409/429; nessun redirect HTML per auth. UUID negli identificatori.

| Metodo/percorso | Accesso e contenuto |
| --- | --- |
| GET /api/health/live | anonimo, processo attivo |
| GET /api/health/ready | anonimo, DB raggiungibile e schema applicato |
| GET /api/auth/csrf | anonimo; {token}, cookie antiforgery; no cache |
| POST /api/auth/login | CSRF; {email,password}; lockout; 204 o 401 |
| POST /api/auth/logout | autenticato, CSRF; 204 |
| GET /api/auth/me | autenticato; id,email, globalPermissions, twoFactorAvailable=false |
| GET /api/devices | autenticato; soli dispositivi con view; lista vuota fino a enrollment M2 |
| GET /api/branding | autenticato; name,logoUrl,primaryColor,theme |
| PUT /api/branding | administration globale + CSRF; medesimi campi; logo URL HTTPS o vuoto, tema light/dark |
| GET /api/admin/users | administration globale; id,email,roles |
| POST /api/admin/users | administration globale + CSRF; {email,password,role} ruolo Viewer/Operator/Administrator; 201 |
| POST /api/admin/grants | administration globale + CSRF; {userId,role,groupId?,deviceId?}; esattamente un ambito; 201 |
| DELETE /api/admin/grants/{id} | administration globale + CSRF; revoca; 204 |
| GET /api/admin/audit | administration globale; ultimi 100 eventi decrescenti |

Permessi distinti: view, remote.control, terminal, files, updates, administration. Endpoint di controllo remoto/terminale/file/update assenti in M1. Grant locale administration non abilita gli endpoint globali. Nessuna registrazione pubblica. Bootstrap amministratore tramite comando dedicato e secret file temporaneo, una sola volta quando non esistono utenti.

M2: enrollment token monouso, heartbeat con identità device separata, gruppi, inventario e metriche. M3: adapter MeshCentral, sessioni effimere dopo autorizzazione, audit ed esplicita revoca; nessuna condivisione credenziali admin MeshCentral con browser. M4: job Windows Update idempotenti con approvazione, stato, timeout e audit. API future non ancora disponibili.
