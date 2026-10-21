# Endpoint Security — Microsoft Defender

Stato: **prima milestone read-only implementata nei sorgenti 0.8.17; non ancora
compilata, verificata o distribuita**. L'Agent raccoglie snapshot di stato e
esclusioni quando esposte dal provider; API, permesso dedicato e scheda
dispositivo sono stati aggiunti. La modifica effettiva delle esclusioni e delle
policy resta fuori ambito.

## Obiettivi e limiti

Il modulo raccoglie lo stato di Microsoft Defender Antivirus e rende visibili le
esclusioni effettive per dispositivo e gruppo. Consente in una fase dedicata di
proporre e applicare modifiche autorizzate, tipizzate, tracciate e con scadenza.
Non disattiva Defender o Tamper Protection, non scrive chiavi di policy per
forzare modifiche e non crea esclusioni proprie per OmniDesk.

Le API documentate da Microsoft utili alla raccolta sono `Get-MpComputerStatus`
per stato e protezioni, `Get-MpPreference` per preferenze/esclusioni e il modulo
Defender `Add-MpPreference` / `Remove-MpPreference` per future operazioni
esplicite. Le esclusioni possono ridurre le scansioni e altre protezioni; per
questo si limitano ai valori più specifici possibili e si rifiutano gli ambiti
troppo ampi. [Stato Defender](https://learn.microsoft.com/en-us/powershell/module/defender/get-mpcomputerstatus), [tipi e impatto delle esclusioni](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-exclusions-overview), [API documentate di configurazione](https://learn.microsoft.com/en-us/powershell/module/defender/add-mppreference).

GPO, Intune, Configuration Manager e Tamper Protection possono prevalere sulle
impostazioni locali o bloccare le modifiche. Il worker deve riportare
`blocked_by_policy`, `tamper_protected` o `management_source_unknown`, spiegare
che la correzione va fatta nel sistema che possiede la policy e terminare senza
tentare fallback o aggiramenti. La provenienza è `unknown` quando non è
attestabile con una fonte supportata: non si deve dedurre che una voce sia
locale solo perché compare nelle preferenze. [Tamper Protection](https://learn.microsoft.com/en-us/defender-endpoint/manage-tamper-protection-intune), [gestione antivirus Intune](https://learn.microsoft.com/en-us/intune/device-configuration/endpoint-security/antivirus), [Group Policy](https://learn.microsoft.com/en-us/defender-endpoint/use-group-policy-microsoft-defender-antivirus).

## Raccolta e operazioni Agent

Il servizio Windows raccoglie un report limitato e validato insieme
all'inventario (primo invio al primo inventario dopo l'avvio):

- disponibilità e stato del provider Defender, modalità/servizio e protezione in
  tempo reale;
- versione delle firme, data dell'ultimo aggiornamento e ultimo quick/full scan
  disponibile;
- stato Tamper Protection; la provenienza della gestione è `unknown` perché
  questa milestone non la determina;
- esclusioni per percorso, processo ed estensione, con limite di 256 voci e
  512 caratteri per valore; origine ed evidenza non sono dedotte.

Il servizio mappa richieste API a operazioni chiuse (`read_status`,
`read_exclusions`, `add_exclusion`, `remove_exclusion`, `evaluate_policy`). Non
accetta script, cmdlet, argomenti PowerShell o percorsi di eseguibili dal server.
Le letture sono read-only. Le mutazioni usano soltanto le API Defender supportate,
una voce per volta, dopo controllo versione, policy effettiva, permessi, consenso
esplicito e idempotency key. Non sostituiscono intere liste e non rimuovono voci
che OmniDesk non ha creato. Gli errori di Tamper Protection/GPO/Intune diventano
esiti auditabili, mai motivo per scrivere nel registro o cambiare policy.

La sincronizzazione invia solo risultati strutturati; il server conserva
l'ultimo snapshot nel payload inventario esistente. I client meno recenti che
non inviano il campo Defender non sovrascrivono l'ultimo snapshot. Il dettaglio
è rimosso dallo status ordinario se manca il permesso. `endpointsecurity.defender.view`
è un permesso distinto da `view` e viene assegnato per default solo al ruolo
Administrator. Il dettaglio è rimosso dalle risposte ordinarie dell'inventario
quando l'utente non ha quel permesso. Questa milestone non conserva cronologia,
non confronta policy desiderate e osservate e non include retention separata.

## Modello dati proposto

Tutte le tabelle sono versionate con migrazioni EF Core. Le identità dispositivo
sono FK alle entità esistenti; i record storici restano consultabili dopo la
revoca dell'Agent.

| Entità | Campi principali | Scopo |
| --- | --- | --- |
| `DefenderSnapshots` | `Id`, `DeviceId`, `CapturedAt`, `ObservedAt`, `ProviderState`, `RealTimeProtection`, `SignatureVersion`, `SignatureUpdatedAt`, `LastQuickScanAt`, `LastFullScanAt`, `TamperProtection`, `ManagementSource`, `Restrictions`, `AgentVersion` | Stato osservato, freschezza e diagnosi senza valori inventati |
| `DefenderExclusionObservations` | `Id`, `DeviceId`, `SnapshotId`, `Kind`, `NormalizedValue`, `Source`, `SourceEvidence`, `ObservedAt`, `ManagedByOmniDesk` | Esclusioni effettive; origine nullable/unknown se non attestabile |
| `DefenderPolicies` | `Id`, `Name`, `ScopeKind`, `ScopeId`, `Revision`, `State`, `CreatedBy`, `CreatedAt`, `PublishedBy`, `PublishedAt` | Politica di gruppo/dispositivo, con revisioni pubblicate immutabili |
| `DefenderPolicyEntries` | `Id`, `PolicyId`, `Kind`, `Value`, `ExpiresAt`, `Reason`, `CreatedBy` | Desiderato dichiarativo con limiti di scadenza |
| `DefenderPolicyAssignments` | `Id`, `PolicyId`, `GroupId` o `DeviceId`, `Enabled`, `AssignedAt`, `AssignedBy` | Ambito assegnato; vincolo esattamente uno tra gruppo/dispositivo |
| `DefenderOperations` | `Id`, `DeviceId`, `PolicyRevision`, `Operation`, `Kind`, `Value`, `Reason`, `RequestedBy`, `RequestId`, `ExpectedSnapshotId`, `ExpiresAt`, `State`, `ResultCode`, `ResultAt` | Coda/esito tipizzato, idempotenza, approvazione e ripristino |

Ogni operazione registra in audit: attore, permesso e ambito, motivazione,
conferma, hash/revisione del payload validato, dispositivo/gruppo, valore
precedente e successivo osservato, timestamp, risultato Windows e messaggio
sanitizzato. Nessun comando eseguibile, credenziale o script entra nell'audit.
La tabella operazioni conserva la transizione richiesta/eseguita/verificata;
una rimozione o un rollback è una nuova operazione collegata all'originale.

Per un'esclusione temporanea, OmniDesk registra prima lo stato precedente. Alla
scadenza rimuove soltanto la voce aggiunta da OmniDesk, se è ancora presente e
non è diventata gestita da una policy esterna. Se il device era offline,
l'operazione resta `expired_pending_removal` finché l'Agent non si riconnette;
la dashboard lo mostra come non conforme. Non si promette la rimozione se GPO,
Intune o Tamper Protection la impediscono.

## Schema API proposto

Rotte browser dietro cookie Identity, antiforgery per ogni mutazione e controllo
server-side default-deny. I nomi sono uno schema di progetto, non endpoint già
disponibili.

| Metodo e rotta | Permesso | Funzione |
| --- | --- | --- |
| `GET /api/devices/{id}/security/defender` | `endpointsecurity.defender.view` | Snapshot, esclusioni e freschezza dispositivo |
| `GET /api/groups/{id}/security/defender` | `endpointsecurity.defender.view` | Aggregati e conformità dei device del gruppo |
| `GET /api/security/defender/policies` | `endpointsecurity.defender.view` | Policy visibili nell'ambito autorizzato |
| `POST /api/security/defender/policies` | `endpointsecurity.defender.policy.manage` | Crea bozza e revisioni |
| `POST /api/security/defender/policies/{id}/publish` | `endpointsecurity.defender.policy.manage` | Pubblica revisione con conferma e revisione attesa |
| `POST /api/devices/{id}/security/defender/exclusions` | `endpointsecurity.defender.manage` | Richiede l'aggiunta tipizzata, `reason`, `requestId`, `expiresAt` e versione attesa |
| `DELETE /api/devices/{id}/security/defender/exclusions/{id}` | `endpointsecurity.defender.manage` | Richiede rimozione tipizzata con motivazione e conferma |
| `POST /api/devices/{id}/security/defender/operations/{op}/rollback` | `endpointsecurity.defender.manage` | Crea operazione inversa se lo stato corrente consente il ripristino |
| `POST /api/agent/security/defender/snapshot` | identità dispositivo | Riceve report strutturato, autenticato e limitato |
| `GET /api/agent/security/defender/operations` | identità dispositivo | Claim di operazioni già autorizzate e valide |
| `POST /api/agent/security/defender/operations/{id}/result` | identità dispositivo | Registra esito e stato osservato |

I permessi sono indipendenti da `administration`, `remote.control` e `updates`,
con ambiti global/group/device verificati dal server. Proposta permessi: view
per consultazione, manage per singole operazioni, policy.manage per creazione e
pubblicazione su gruppi. Una mutazione mostra in anteprima tipo/valore esatto,
ambito, impatto, scadenza e device interessati; richiede motivo obbligatorio e
conferma finale. Per policy di gruppo ad alto impatto è consigliata approvazione
separata da un secondo operatore.

## Validazione di sicurezza delle richieste

Il server e l'Agent applicano validazione indipendente e idempotente:

- path solo assoluti, normalizzati, locali e specifici; rifiutare radici disco,
  directory Windows/sistema, profili utente, cartelle temporanee, wildcard
  generiche, traversal, device path e UNC non approvati;
- process exclusion solo con percorso completo dell'eseguibile; rifiutare nomi
  immagine senza path e percorsi di sistema/strumenti amministrativi;
- extension exclusion limitate a estensioni approvate, mai estensioni eseguibili
  o archivi a rischio elevato;
- scadenza predefinita 24 ore, massimo proposto 30 giorni; policy permanente
  richiede approvazione separata e motivazione documentata;
- blocco di duplicati confliggenti, valori non canonicalizzabili, estensioni
  wildcard e ogni esclusione che ecceda il limite di ampiezza configurato.

Le liste di valori vietati sono policy versionate e testate; non vengono usate
per auto-autorizzare altri elementi. Se lo stato effettivo cambia tra anteprima
e claim dell'Agent, il server rifiuta la revisione obsoleta e richiede una nuova
conferma.

## Interfaccia proposta

Nuova area **Sicurezza endpoint → Microsoft Defender** con icona scudo:

- panoramica dispositivo con card Defender attivo, real-time protection,
  firme aggiornate, ultimo controllo e timestamp dell'ultimo report;
- tabella esclusioni con tipo, valore, origine attendibile/ignota, gestione
  OmniDesk o esterna, scadenza e stato;
- vista gruppo con totale device, conformi, non conformi, dati obsoleti e policy
  bloccate; drill-down al singolo computer;
- confronto stato desiderato/osservato e timeline audit per ogni modifica;
- procedura guidata di aggiunta/rimozione con preview, motivo, scadenza e
  conferma esplicita; i controlli diventano sola lettura quando le policy
  esterne o Tamper Protection impediscono la modifica;
- stati accessibili: `compliant`, `drifted`, `pending`, `blocked_by_policy`,
  `tamper_protected`, `offline`, `stale`, `unsupported`, `unknown`.

Nessun controllo UI permette di disattivare l'antivirus, disattivare Tamper
Protection, ignorare una policy esterna o autorizzare automaticamente il
software OmniDesk.

## Installazione e aggiornamento dell'Agent: gap analysis

L'integrazione Defender va coordinata con il lifecycle MSI, senza trasformare
l'installer in uno strumento di esclusione. Stato al 20 ottobre 2026:

| Requisito | Stato osservato | Lavoro necessario |
| --- | --- | --- |
| MSI elevato e servizio Windows con ACL/recovery | Implementato nel pacchetto 0.8.16; CI ha test lifecycle su runner Windows | Conservare test install/upgrade/repair/rollback e aggiungere prova Windows 10 reale |
| Integrità durante download/aggiornamento | Hash SHA-256 e dimensione sono nel manifest; API ricontrolla ZIP/MSI e Agent verifica il MSI staged | Mantenere controlli prima di ogni esecuzione e testare corruzione/troncamento/downgrade |
| Firma digitale Agent, Tray, Setup e MSI | Non disponibile; kit attuale unsigned da laboratorio | Aggiungere firma Authenticode in pipeline quando il certificato verrà fornito fuori dal repository; verificare catena, publisher e timestamp; rifiutare firme non valide. Hash non sostituisce identità del publisher |
| Diagnostica Defender | Migliorati i log Agent per accesso negato e file/servizio mancanti | Correlare, quando leggibile, gli eventi di sicurezza con il job MSI, il percorso e la finestra temporale. Classificare `defender_blocked` solo con evidenza corrispondente; altrimenti `installer_failed`/`unknown` |
| Stato dell'aggiornamento nella dashboard | Mostra job, durata, risultato generico, riavvio richiesto e messaggio MSI | Aggiungere categorie Defender, verifica firma/hash, rollback, servizio non recuperato, timeout e remediation amministrativa con timestamp e versione precedente/nuova |
| Aggiornamento atomico e rollback affidabile | Windows Installer gestisce la transazione MSI; test CI coprono upgrade/repair/rollback da baseline storiche. Il worker attuale controlla exit code e versione installata | Conservare in cache protetta l'ultimo MSI known-good; aggiungere health check post-install di servizio e versione; se fallisce, un solo rollback verificato al pacchetto precedente ammesso dalla versione minima, poi verifica e ricevuta |
| Eccezioni Defender | Non vengono create automaticamente; la quarantena attuale è un blocco confermato | L'UI documenta Protection History/console Intune e registra che l'azione deve essere dell'amministratore. Una futura eccezione mirata passa dalla milestone Endpoint Security e dagli strumenti supportati, con motivazione/scadenza; mai tramite bootstrap MSI |
| GPO, Intune, Tamper Protection | Il codice Agent non le aggira | Rilevare/riportare il blocco come ownership esterna; nessuna scrittura registro, disabilitazione o fallback locale che contraddica la policy |

La versione 0.8.13 è stata compilata e ha superato nella CI Windows `39408495812`
build, install/recovery/uninstall e upgrade/repair/rollback da baseline 0.6.0 e
0.7.0. La CI Linux/browser `39408495830` è passata. Il bundle 0.8.13 è promosso
al download autenticato nel volume `/packages`; il bundle 0.8.12 e la relativa
manifestazione sono conservati per rollback. SHA-256 ZIP
`63b2711c51950b7cc8dabd1e508a756de089b38eea25aac6345c88468de345b7`; MSI
`fa46d9dda4aa4e6e7095981c2138a096847300ab3d86c02d395066f37c930ab3`.
Il container API ha letto la nuova manifestazione, il checksum nel volume
corrisponde, readiness HTTPS è 200 e il download anonimo resta 401. Non è stato
riavviato alcun servizio. La build rimane unsigned da laboratorio, contiene lo
stesso motore MeshCentral rilevato da Defender e non è stata installata sul PC
pilota: CI verde non equivale a un test runtime Defender/desktop.

### Flusso proposto per l'aggiornamento

1. Pubblicare artifact MSI firmati, manifest versionato e checksum; aggiornare
   il puntatore alla release con rename atomico dopo verifica del bundle. La
   pipeline mantiene l'artifact precedente per rollback.
2. Il server autorizza l'operazione e consegna al device soltanto un MSI
   esistente nel manifest approvato, con dimensione, versione e SHA-256.
3. L'Agent scarica in una directory staging ACL-protetta, controlla dimensione,
   hash e firma/publisher secondo il canale release, poi registra receipt e
   versione precedente. Nessun pacchetto viene avviato in streaming.
4. L'updater avvia `msiexec` elevato dal servizio, senza riavvio automatico di
   Windows. Dopo l'installazione controlla versione, stato del servizio e
   completamento di un heartbeat/health check con deadline definita.
5. Se l'installazione fallisce prima del commit, MSI esegue il suo rollback. Se
   la transazione è terminata ma il servizio non supera il controllo salute,
   l'updater ripristina una sola volta il precedente MSI verificato, se ancora
   consentito dalla policy di versione minima. Poi verifica di nuovo servizio e
   versione. Un doppio fallimento lascia il servizio in recovery Windows,
   registra un errore esplicito e richiede intervento amministrativo; non entra
   in un ciclo di retry.
6. L'Agent invia ricevuta con risultato installazione/rollback e categoria
   diagnostica. Se Defender ha messo in quarantena un file, la dashboard mostra
   evidenza disponibile e i passaggi per l'amministratore. OmniDesk non ripristina
   file quarantinati, non crea esclusioni e non disattiva protezioni.

Questa strategia è transazionale con controllo salute e rollback bounded; non
promette uno scambio binario senza interruzione. Le fasi staging/install/health
devono essere visibili e distinguere un job ancora senza ricevuta da un errore.

### Diagnostica e test richiesti

L'API risultato Agent dovrebbe aggiungere enum validato `failureCategory`,
`previousVersion`, `targetVersion`, `installedVersion`, `rollbackState`,
`windowsInstallerExitCode` e riferimenti minimizzati agli eventi Defender. La
classificazione usa codice errore e, se disponibile, eventi Defender 1116/1117
correlati nel tempo al job e al file/hash atteso; non dipende dal testo
localizzato del messaggio. Nessuna corrispondenza attendibile produce `unknown`,
non un falso verdetto di quarantena.

La milestone install/upgrade deve coprire MSI alterato, hash o firma errati,
file/servizio rimossi, errore Windows Installer, avvio servizio fallito,
heartbeat non ricevuto, rollback riuscito/fallito, riavvio sistema richiesto e
evento Defender correlato/non correlato. Test di classificazione Defender usano
eventi simulati; non introducono esclusioni né disattivano Defender sul runner.
La prova su PC reale verifica il percorso amministrativo e Protection History;
policy Intune/GPO/Tamper Protection si testano in VM gestite e si accetta come
esito corretto il blocco rispettato e comunicato.

## Milestone e criteri di accettazione

1. **Design/API**: specifica architetturale e limiti registrati.
2. **Read-only** (sorgenti 0.8.17, da compilare e verificare): collector
   tipizzato, snapshot nell'inventario, permesso dedicato e scheda dispositivo.
   Copre stato e esclusioni osservati; non include vista gruppo, cronologia,
   origine della policy o modifiche Defender. Nessuna policy o esclusione viene
   cambiata.
3. **Mutazioni controllate**: agent operation allowlist, RBAC e antiforgery,
   motivo/conferma, scadenza/ripristino, conflitti GPO/Intune/Tamper, audit e
   test unitari/integration Windows; prova manuale su Windows 10/11 e policy
   gestite prima dell'abilitazione.
4. **Policy di gruppo**: pubblicazione revisionata, rollout pilota, conformità
   periodica, limiti di velocità e rollback per gruppo; abilitazione solo dopo
   i gate di sicurezza della milestone 3.
5. **Installer compatibile Defender**: Authenticode quando il certificato sarà
   disponibile, verifica firma/hash, classificazione eventi, status dashboard,
   health check post MSI e rollback bounded, con gate Windows CI e pilota.

La milestone read-only è implementata nei sorgenti, ma resta da compilare,
verificare e distribuire. Non sono state eseguite modifiche a Defender o ai
device.
