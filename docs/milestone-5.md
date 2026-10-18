# M5 — console, layout personali e TOTP

Console Windows WinForms/WebView2: stessa dashboard HTTPS e autorizzazioni server, origine obbligatoria `--server`, opzionale origine Mesh `--mesh` per link remoti nel browser esterno. Navigazioni interne limitate all'origine server; nessun bypass certificato, permessi browser negati, download dalla console principale disabilitati. Profilo WebView2 in LocalAppData, nessun segreto embedded. Richiede Evergreen WebView2 Runtime e fiducia CA LAN anche nel client Windows. Progetto e pipeline pubblicazione win-x64 presenti; esecuzione UI su Windows e firma ancora da collaudare.

```powershell
.\OmniDesk.Console.Windows.exe --server https://192.168.1.233:8085
```

Dashboard: layout personale (ordine/visibilità dei tre riquadri iniziali) persistente per utente, indipendente dal branding globale. API GET/PUT /api/dashboard/layout con {widgets:[devices,permissions,authentication]}, sottoinsieme ordinato senza duplicati. Nessuna metrica fittizia nei widget.

TOTP implementata usando i provider ASP.NET Identity, **disattivata per default** con OMNIDESK_TOTP_ENABLED=false. Per collaudo attivare true e ricreare il container API. Setup autenticato con password corrente, chiave generata da Identity, conferma TOTP prima dell'attivazione; login richiede password + codice o recovery code. Recovery code monouso e rotazione; disattivazione richiede password+TOTP. Un account già abilitato non può accedere con sola password se la feature viene disabilitata sul server: riabilitare il flag, mai bypassare MFA.

Le chiavi Authenticator e i recovery code vengono cifrati prima di persistere in AspNetUserTokens tramite DataProtection. Backup volume identity_keys obbligatorio: perdere le chiavi impedisce la decifratura. La cifratura protegge il DB separato dal volume, non da un amministratore host che possa leggere entrambi. Nessun servizio QR esterno; inserimento manuale chiave nell'app Authenticator. Segreti e recovery code mostrati in UI solo in memoria, cancellati quando si cambia scheda/logout; risposte no-store. Il codice TOTP ha la finestra di validazione del provider Identity; non implementato un registro anti-riuso dei codici TOTP. Lockout anche sulle riautenticazioni fallite.

API: POST /api/auth/totp/setup {password}; /enable e /disable {password,code}; /recovery-codes {password,code}; login {email,password,code?,recoveryCode?}. POST /api/auth/password {currentPassword,newPassword,code?}: aggiorna security stamp e rinnova la sessione corrente; altre sessioni invalidate al successivo controllo stamp (massimo un minuto). Tutte le mutazioni richiedono CSRF, auditate senza segreti. Recovery login disponibile; reset assistito in caso di perdita totale dei fattori ancora da progettare.

Fonti: [Identity MFA](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/mfa?view=aspnetcore-10.0), [WebView2 WinForms](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winforms).
