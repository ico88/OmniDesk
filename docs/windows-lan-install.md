# Agent Windows su LAN

Queste istruzioni descrivono il kit diagnostico 0.5.2 già compilato. I [sorgenti Agent/Tray 0.6.0](windows-agent-tray-implementation.md) sono un avanzamento successivo, non compilato o distribuito; non sostituire il kit con sorgenti non verificati.

Server di collaudo: **https://192.168.1.233:8085**. Agent 0.5.2 conserva IP/porta, usa HTTPS con verifica standard del certificato e rifiuta redirect. Servono Windows x64, orologio sincronizzato, accesso TCP dal client alla porta 8085 e privilegi amministrativi per MSI/enrollment/store certificati. Nessuna porta inbound aggiuntiva è necessaria sul PC per heartbeat/inventario. Non vengono modificate automaticamente regole firewall.

## Stato delle consegne

Agent 0.5.2 self-contained win-x64 cross-compilato; codice connessione testato sul server LAN tramite harness Linux con CA esplicita. Script PowerShell verificato sintatticamente con PowerShell 7.5; esecuzione Windows, parsing 5.1 e nuovo MSI richiedono runner/VM Windows. WiX dichiara Windows come unica piattaforma supportata: tentativo locale Linux fallito, non è stato prodotto un nuovo MSI. La pipeline Windows aggiornata pubblicherà MSI e script dopo esecuzione; nessuna esecuzione CI nuova attestata qui.

Il kit diagnostico locale `artifacts/agent-lan/OmniDesk.Agent.LAN-Diagnostics.zip` contiene eseguibile, script, CA pubblica e impronte; **non contiene MSI** e non installa autonomamente un servizio. L'MSI 0.5.1 precedente non contiene i nuovi comandi richiesti dallo script. MSI e binario restano unsigned per laboratorio: firma e install/upgrade/uninstall richiedono collaudo. Tray e desktop remoto non sono inclusi in questo incremento.

## Preparazione

1. Procurarsi l'MSI **0.5.2** dal build Windows e trasferirlo al PC insieme a `installer/Install-OmniDeskAgent.ps1` e alla CA pubblica `omnidesk-local-ca.crt`. Conservare token e installer in una cartella privata. Rispettare la policy aziendale di firma/esecuzione script; lo script non modifica ExecutionPolicy.
2. Confrontare l'impronta SHA256 della CA con quella ottenuta indipendentemente sul server. Il digest riguarda il certificato DER, non i byte del file PEM:

```bash
openssl x509 -in omnidesk-local-ca.crt -noout -fingerprint -sha256
```

Rimuovere i due punti dall'impronta prima di passarla allo script. Non assumere autenticità solo perché impronta e CA sono nello stesso ZIP. Non distribuire chiavi private CA/Identity o password server. Per il pacchetto unsigned di laboratorio verificare anche SHA256 MSI da una fonte affidabile: il checksum fornisce integrità rispetto al valore atteso, non firma del produttore.

3. Dalla dashboard amministratore, **Dispositivi → Registra un dispositivo**, selezionare gruppo e generare token monouso. Salvare il token in un file privato sul PC Windows; non passarlo in chiaro alla riga di comando.

## Installazione elevata

Esempio in PowerShell amministrativo, sostituendo le impronte con quelle realmente verificate:

```powershell
.\Install-OmniDeskAgent.ps1 `
  -MsiPath C:\Secure\OmniDesk.Agent.msi `
  -ServerUrl https://192.168.1.233:8085 `
  -CaCertificatePath C:\Secure\omnidesk-local-ca.crt `
  -CaSha256 '<64-cifre-esadecimali-verificate>' `
  -EnrollmentTokenFile C:\Secure\enrollment.txt `
  -TrustServerCa
```

Il percorso ordinario richiede firma MSI valida. Solo per laboratorio unsigned aggiungere `-AllowUnsignedForTesting -MsiSha256 '<SHA256-MSI-verificato>'`; firme invalide vengono rifiutate anche in questa modalità. L'importazione CA nello store **LocalMachine\Root** richiede opt-in `-TrustServerCa` e impronta corretta; lo store del solo utente non basta per il servizio LocalSystem.

Lo script prepara staging con ACL SYSTEM/Administrators, verifica hash/firma MSI e CA, importa il certificato pubblico se autorizzato, installa tramite MSI senza reboot automatico, controlla HTTPS/readiness e solo dopo esegue enrollment. Verifica ACL/DPAPI/URL dell'identità protetta e che il servizio sia avviato. Un'identità esistente dello stesso server viene preservata e verificata, senza consumare un altro token; cambio server o re-enrollment richiedono revoca/dismissione esplicite.

Lo staging viene eliminato al termine; il file token originale rimane sotto controllo dell'amministratore e va rimosso dopo enrollment riuscito. L'eliminazione non garantisce cancellazione sicura su disco. Un errore successivo all'importazione CA non rimuove automaticamente il trust; un errore dopo consumo token richiede verifica/revoca dal server prima di riprovare. Installazione e enrollment sono passaggi distinti, non una transazione distribuita.

## Diagnostica e verifica operativa

Il kit diagnostico può essere estratto senza installare il servizio. Dopo aver predisposto il trust del certificato secondo la procedura aziendale:

```powershell
.\OmniDesk.Agent.Windows.exe connect-test --server https://192.168.1.233:8085
```

Non consuma token, non genera identità e non modifica servizi. Verifica HTTPS e readiness, non le credenziali dispositivo o l'upload. Dopo installazione, il comando elevato seguente controlla identità locale, ACL e leggibilità DPAPI senza stampare segreti:

```powershell
& 'C:\Program Files\OmniDesk Agent\OmniDesk.Agent.Windows.exe' verify-enrollment --server https://192.168.1.233:8085
Get-Service OmniDeskAgent
```

Attendere circa un minuto e verificare sulla dashboard **heartbeat reale e inventario**. Provare reboot senza logon e disconnessione/ritorno rete. Un connect-test eseguito dall'amministratore non dimostra da solo trust/proxy/rete del servizio LocalSystem. In caso di errore controllare routing/proxy, CA LocalMachine, clock, token scaduto/consumato e revoca dispositivo; non disabilitare TLS.

## Confine desktop remoto

La connessione LAN dell'Agent abilita enrollment, heartbeat/inventario e protocollo job esistente; non installa né configura Mesh agent. Desktop remoto operativo richiede ancora motore MeshCentral, soluzione del gate dipendenze, provisioning/mapping e collaudo Windows/NAT/consenso/revoca tunnel. L'Agent non viene dichiarato un motore desktop remoto.

### Preparazione sorgenti MSI Agent + Tray 0.6.0

Da un checkout su Windows x64 con .NET SDK 10, eseguire senza elevazione:

```powershell
.\installer\Build-OmniDeskWindows.ps1
```

Lo script compila Agent e Tray, esegue il self-test Agent, genera il singolo MSI e copia MSI, script di installazione e checksum in `artifacts/windows-0.6.0`. È richiamato anche dal workflow `windows-agent-package`, avviabile manualmente. Non installa servizi né modifica certificati. Il pacchetto è unsigned per laboratorio; i checksum non sono una firma. L'installazione elevata e la CA esplicita restano gestite dalla procedura precedente. Questo percorso è stato scritto ma non eseguito: l'MSI 0.6.0 non è ancora disponibile e non contiene ancora il provisioning Mesh agent o ticket/screenshot.

### Aggiornamento: artifact Windows 0.6.0 generato

Il workflow Windows sul branch `feature/windows-support-lan` ha ora generato l'MSI Agent + Tray. Usare l'artifact del run indicato nel registro validation, verificando il commit; il kit diagnostico 0.5.2 precedente rimane distinto e non contiene questo MSI. Il nuovo MSI comprende le funzionalità ticket/screenshot descritte in [supporto](support-implementation.md), ma non installa automaticamente l'agente MeshCentral: non è un pacchetto desktop remoto completo.

Per il collaudo LAN servono l'MSI, `Install-OmniDeskAgent.ps1`, la CA pubblica del server e un token enrollment generato dalla console. Verificare il checksum contro l'artifact GitHub, quindi eseguire da PowerShell elevata:

```powershell
.\Install-OmniDeskAgent.ps1 -MsiPath .\OmniDesk.Agent.msi -ServerUrl https://192.168.1.233:8085 -CaCertificatePath .\omnidesk-local-ca.crt -CaSha256 45926C7ACA3D49E6619E377AD0FA2110951F6F0E54DF9B83A6C6E3AD712BE5B2 -TrustServerCa -AllowUnsignedForTesting -MsiSha256 '<SHA256 MSI verificato>' -EnrollmentTokenFile .\enrollment-token.txt
```

Il file token va creato e protetto secondo la procedura precedente; non inviarlo in chat o committarlo. Non inserire letteralmente il placeholder del checksum. Al termine eliminare il file originale del token. Lo script non avvia la Tray nel contesto elevato: eseguire il logoff/logon o avviarla separatamente dalla sessione standard. L'uscita dalla Tray deve lasciare attivo il servizio.

Verificare quindi: dispositivo e heartbeat nella console, stato Tray, richiesta ticket senza screenshot, seconda richiesta con anteprima/consenso, visibilità dalla console IT e risposta/stato aggiornati nella Tray. Questi sono criteri di collaudo Windows/LAN, non risultati già attestati.

Per dismettere: un amministratore OmniDesk revoca l'identità dispositivo dalla console e verifica la revoca; un amministratore Windows rimuove il prodotto tramite MSI. La rimozione locale da sola non sostituisce la revoca server. La CA di laboratorio rimane installata intenzionalmente dopo uninstall: la sua rimozione amministrativa va pianificata considerando gli altri servizi che la usano. Nessuna protezione assoluta contro amministratori locali o accesso fisico privilegiato.

### Kit 0.7 dalla console

Nella UI autenticata come amministratore: Dispositivi → Scarica Agent Windows. Il riquadro mostra la versione effettivamente pubblicata, dimensione e SHA256. Il server verifica l'integrita prima di inviare il file; nessun download anonimo. Il kit 0.7 contiene anche il motore remoto nativo, senza configurazione o credenziali per dispositivo. Windows non richiede Node.js: Node/npm servono soltanto alla build.

Dopo enrollment e heartbeat, aprire il dispositivo → Motore e storico sessioni remote → Abilita motore sul dispositivo. Il servizio recupera la configurazione approvata e registra il nodo; usare Aggiorna sessioni per verificare il mapping. Solo dopo l'associazione aprire desktop/terminale/file con il rispettivo permesso e il consenso dell'utente. Motore LAN su HTTPS 192.168.1.233:8086, console su :8085, stessa CA pubblica. Client di collaudo concordato Windows 10 su 192.168.1.160.

Se Windows blocca lo script per la provenienza Internet, verificare prima i checksum, leggere il contenuto e sbloccare quel singolo file tramite Proprieta → Sblocca. Non occorre abbassare la policy di esecuzione globale. Il pacchetto resta unsigned di laboratorio: firma produzione e collaudi interattivi non derivano dal download o dall'installazione CI.

### Installazione manuale guidata dal kit aggiornato

Il kit aggiornato contiene `Installa-OmniDesk.ps1`: avviarlo da PowerShell
amministrativa nella cartella estratta. Server, CA e checksum MSI sono gia
impostati nel launcher. Generare il token dalla console e incollarlo quando
richiesto (input mascherato), senza creare enrollment-token.txt. Il token viene
scritto solo nello staging SYSTEM/Administrators e rimosso nel finally, senza
passarlo come argomento al processo. L'opzione EnrollmentTokenFile resta
supportata per automazioni; le istruzioni precedenti descrivono tale modalita.
Non modificare policy globali: se necessario usare RemoteSigned con Scope Process
e sbloccare i due script verificati, come indicato nel LEGGIMI del kit.

### Aggiornamento correttivo Tray 0.7.1

La 0.7.0 usa erroneamente WindowsIdentity.Groups per cercare il SID logon;
.NET filtra quel gruppo. Il servizio puo inviare heartbeat mentre la Tray
mostra servizio/stato server non disponibile. La 0.7.1 legge TokenLogonSid
nativo anche nel controllo del client impersonato, senza ampliare le ACL.
Per aggiornare: chiudere solo l'icona Tray, estrarre il nuovo kit e avviare
Installa-OmniDesk.ps1 da PowerShell amministrativa. Non disinstallare e non
revocare il dispositivo: sullo stesso server lo script conserva l'identita
protetta esistente e non richiede un altro token. Dopo l'aggiornamento avviare
la Tray dalla sessione ordinaria (oppure logoff/logon) e verificarne lo stato.
Queste sono istruzioni di collaudo: registrare esito CI e prova PC in validation.

### Sorgente MSI per manutenzione

Lo script aggiornato conserva agent.msi verificato nella propria directory
privata ProgramData/OmniDesk-Install-* per consentire repair e manutenzione MSI.
Token e CA temporanei sono rimossi come prima. Non eliminare tale sorgente mentre
il prodotto resta installato. I kit precedenti eliminavano anche il pacchetto e
potevano causare errore 1706 in repair: usare la copia originale del medesimo MSI
con recache/repair, senza rimuovere l'identita dispositivo. La conservazione della
sorgente non sostituisce il backup delle chiavi o una firma di produzione.

### Agent 0.7.2 e Windows Update

La 0.7.2 conserva la sorgente MSI privata e avvia il worker PowerShell locale con
RemoteSigned limitato al processo (senza prevalere sulle Group Policy). Gli
errori futuri includono exit code/categoria invece del solo process failed.
Scaricare il kit 0.7.2 quando pubblicato, chiudere la Tray e avviare il launcher
amministrativo nella nuova cartella estratta; identita e token esistenti restano
validi sullo stesso server. Riavviare se MSI restituisce 3010. Poi provare una
nuova scansione dalla sezione Windows Update; la vecchia scansione fallita resta
nello storico e non viene rieseguita automaticamente. Nessun install/reboot
viene autorizzato da una sola scansione. Il collaudo reale va registrato a parte.
L'accesso non presidiato dipende dalla policy server e non richiede questa
versione per essere selezionato sulla console.
