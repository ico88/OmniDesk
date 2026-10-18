# Milestone 2 — enrollment e agente

## Protocollo implementato

Amministratore globale crea gruppo e token con TTL 1–1440 minuti (default 30). Token casuale 256 bit, mostrato solo all'emissione; DB conserva SHA-256, ambito, scadenza, consumo e revoca. L'enrollment consuma il token in transazione con aggiornamento condizionale: due richieste concorrenti non possono creare due dispositivi.

Il servizio Windows genera RSA 3072, invia solo la chiave pubblica e conserva la chiave privata cifrata tramite DPAPI LocalMachine in `%ProgramData%\OmniDesk\agent.json`, ACL limitate a SYSTEM e Administrators. JWT RS256 di durata 60 secondi, issuer/subject device UUID, audience omnidesk-agent, jti casuale nuovo per richiesta. Il backend verifica algoritmo, firma, issuer, audience e durata massima 90 secondi; usa PostgreSQL come cache anti replay condivisa, con pulizia periodica. Revoca verificata nel DB a ogni richiesta. Chiavi pubbliche non sono segreti. Gli endpoint agent usano schema distinto dal cookie umano e sono i soli esenti da CSRF; enrollment ha rate limit.

Solo HTTPS con verifica certificato standard; nessun bypass TLS nell'agente. Redirect HTTP disabilitati per non trasferire token/asserzioni a un'altra origine. Nessuna password amministrativa nell'MSI. Token temporanei da file, mai da argomento in chiaro o registry MSI. Enrollment deve essere eseguito elevato. Se il server consuma il token ma la risposta si perde, revocare la vecchia identità ed emettere un nuovo token: recovery idempotente ancora da introdurre.

## API

- GET/POST `/api/admin/groups` (Name).
- GET/POST `/api/admin/enrollment-tokens` ({groupId?,expiresInMinutes?}); DELETE `/{id}` per revoca.
- POST `/api/admin/devices/{id}/revoke`.
- POST `/api/agent/enroll` ({token,name,publicKeyPem}).
- POST `/api/agent/metrics`, PUT `/api/agent/inventory`: JWT device, identità derivata dalla firma; nessun deviceId accettato nel payload.
- GET `/api/devices/{id}/status`: view sul dispositivo; metriche/inventario/online/revoked.

Metriche ogni ~60 secondi con jitter/backoff fino a 15 minuti; CPU tramite GetSystemTimes (prima lettura null), RAM GlobalMemoryStatusEx, dischi fissi, contatori rete cumulativi e uptime GetTickCount64. Inventario ogni 24 ore: WMI computer/BIOS/CPU, software HKLM uninstall a 32/64 bit (non Win32_Product, non software per utenti non caricati). Errori parziali inventario riportati come warnings. Nessuna metrica sintetica. Il DB conserva l'ultimo snapshot, non uno storico; online significa heartbeat ricevuto entro tre minuti e identità non revocata.

## Installazione prevista Windows x64

MSI per-machine WiX, un servizio LocalSystem, binario .NET self-contained. Configurazione identità preservata dalla disinstallazione: revocare sul server e gestire esplicitamente la rimozione locale. Il pacchetto attuale include solo OmniDesk Agent; Mesh agent sarà aggiunto in M3 dopo collaudo del provisioning.

```powershell
msiexec /i OmniDesk.Agent.msi
& 'C:\Program Files\OmniDesk Agent\OmniDesk.Agent.Windows.exe' enroll --server https://rmm.example.com --token-file C:\Secure\enrollment.txt
```

MSI unsigned compilato nella pipeline GitHub Actions Windows; non eseguire questi comandi su endpoint reali finché firma e installazione/upgrade/uninstall non sono verificati. La pipeline Windows prepara un artefatto unsigned; firma Authenticode e aggiornamento automatico sicuro richiedono certificato/code signing e una milestone dedicata. Le modifiche ai servizi Windows sono operazioni di installazione, non sono state eseguite dall'agente di sviluppo sull'host.

Fonti: [Windows Service .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service), [WiX](https://docs.firegiant.com/wix/). Risultati effettivi di build/test in validation.md.

Orologi server/device da sincronizzare: tolleranza asserzioni dieci secondi. Un 401 richiede verifica clock/revoca e riavvio controllato del worker; non implica sempre necessità di nuovo enrollment. MSI versione 0.5.0 per distinguere questo avanzamento dal primo 0.2.0.

Hardening identità locale: Load rifiuta file/directory reparse point, proprietari diversi da SYSTEM/Administrators e ACL Allow per altri SID. Le scritture di identità, script e ricevute usano file temporanei univoci CreateNew, owner/DACL espliciti e sostituzione atomica. Evita di affidarsi a cartelle/file precreati da utenti non privilegiati. Una configurazione legacy con ACL/owner diversi viene rifiutata: verificare il server/device e correggere i permessi con amministratore durante il collaudo upgrade, senza concedere fiducia automaticamente. Test ACL su directory temporanea del runner Windows; nessun servizio di sistema modificato. MSI 0.5.1 distingue questa correzione dalla 0.5.0.
