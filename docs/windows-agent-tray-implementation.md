# Agent e Tray — registro implementazione e stato corrente

**Le sezioni 0.6.0 seguenti sono un registro storico e non descrivono lo stato attuale.** Il branch contiene Agent e Tray distinti nel pacchetto MSI 0.8.9. CI Windows ha verificato build, installazione, Service Recovery, uninstall e upgrade/repair/rollback su runner isolato. Il test client interattivo, la firma Authenticode e il gate Microsoft Defender restano aperti. Per evidenza aggiornata vedere [completion-status.md](completion-status.md), [roadmap.md](roadmap.md) e [validation.md](validation.md).

## Snapshot storico dell'incremento 0.6.0

Le sezioni seguenti fino a “Aggiornamento ticket e compilazione” descrivono lo snapshot 0.6.0 e i suoi limiti al momento della stesura; alcune funzioni lì segnate come non implementate sono state aggiunte nelle release successive. Non usarle per stabilire lo stato corrente: consultare la sezione iniziale e i report linkati.

## Contratto e confini 0.6.0

Target iniziale da collaudare: Windows 11 x64, servizio LocalSystem e Tray per logon senza elevazione. Nuovi progetti `OmniDesk.Windows.Ipc` e `OmniDesk.Tray.Windows`, separati dalla console IT WebView2. La Tray dichiara asInvoker e rifiuta l'esecuzione elevata; una sola istanza per sessione mediante mutex Local. Non riceve chiavi macchina, token o percorsi da aprire con privilegi del servizio.

Protocollo v1 tipizzato in `OmniDesk.Contracts/TrayProtocol.cs`: lunghezza little endian di quattro byte, JSON di massimo 4096 byte/profondità 8, rifiuto proprietà sconosciute e duplicate. Unico metodo ammesso GetConnectionStatus, request ID obbligatorio e correlato nella risposta. Nessun metodo per arresto, configurazione, uninstall, shell, trasferimento file, cattura schermo o ticket. I test sorgente per framing e rifiuto comandi sono predisposti, non eseguiti.

La risposta espone stato, ultimo heartbeat confermato e presenza enrollment, senza identità umana o segreti. L'Agent aggiorna l'ultimo heartbeat solo dopo una risposta di successo all'upload metriche; lo stato non deriva dall'esistenza della pipe. Il modello distingue starting/not_enrolled/connecting/online/degraded/offline/auth_rejected; online/degraded scadono dopo tre minuti senza nuovo heartbeat. Dopo autenticazione rifiutata il worker non avvia enrollment automaticamente.

## Pipe locale per logon

Il servizio enumera i logon tramite WTS (massimo 128) e mantiene una pipe per sessione, rinnovata al cambio logon. WTSQueryUserToken richiede l'identità/privilegi del servizio LocalSystem: eseguire il worker manualmente come normale amministratore non equivale al servizio installato.

Creazione nativa con FIRST_PIPE_INSTANCE, modalità asincrona e REJECT_REMOTE_CLIENTS; handle mantenuto tra connessioni. DACL protetta: SYSTEM full control, logon SID con soli diritti dati/read-attributes/read-control/synchronize, senza create-instance o generici write. La richiesta viene letta con limite e timeout di tre secondi; impersonazione sincrona esclusivamente per verifica user SID e logon SID, poi ripristino prima di ogni operazione. Il servizio ricontrolla che il logon sia corrente; una richiesta per connessione, pausa minima di un secondo, una connessione concorrente per logon.

Il client usa esclusivamente pipe locale e livello Identification. Prima di inviare il frame verifica PID del server contro il servizio SCM OmniDeskAgent in esecuzione, owner SYSTEM della pipe e ImagePath registrato uguale al percorso predefinito Program Files\\OmniDesk Agent\\OmniDesk.Agent.Windows.exe; ricontrolla PID dopo le verifiche. Un percorso MSI personalizzato non è supportato dal client attuale: fallisce chiuso. Gli errori della pipe non autorizzano fallback meno restrittivi o arresto del monitoraggio; la Tray mostra servizio non disponibile/non verificato.

Scelte basate su [sicurezza named pipe Microsoft](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights). La DACL non autentica il singolo eseguibile dello stesso logon: l'unica operazione è quindi sicura anche con un client ostile. Non viene promessa protezione contro amministratore locale o accesso fisico privilegiato.

## Interfaccia e pacchetto

NotifyIcon e menu di stato, ultimo heartbeat, aggiornamento manuale e chiusura della sola icona. Poll ogni 15 secondi, timeout tre secondi e richieste non sovrapposte. Chiusura/crash/logoff Tray non inviano comandi al servizio. Richiedi assistenza è esplicitamente non disponibile: non vengono creati ticket fittizi o catturati screenshot. Non è implementato consenso remoto.

Sorgente MSI 0.6.0: un'unica feature installa Agent e Tray self-contained nella directory protetta Program Files; HKLM Run avvia Tray al prossimo logon come utente. Nessun avvio UI elevato nella sessione dell'installatore. Il MSI resta per-machine, stessa UpgradeCode, senza credenziali nelle proprietà. Agent e Tray sono compilati dalla pipeline Windows prima del packaging.

DACL esplicita del servizio: SYSTEM/Administrators full control, Interactive Users solo query config/status e read-control, senza stop/change-config/start/delete/write-DAC/write-owner. Non usa deny generici. Directory/binari ereditano il confine Program Files del percorso predefinito, ancora da collaudare. Recovery sorgente: restart dopo 5/30/120 secondi, reset conteggio 24 ore, su install/repair; nessun reboot o comando arbitrario. Applicazione effettiva, rollback, stop intenzionale e comportamento dopo fault non-crash devono essere verificati; non si assume che ogni arresto del worker venga trattato come crash da SCM. Riferimenti: [PermissionEx](https://docs.firegiant.com/wix/schema/wxs/permissionex/), [ServiceConfigFailureActions](https://docs.firegiant.com/wix/schema/wxs/serviceconfigfailureactions/).

## Verifiche ancora necessarie nell'incremento 0.6.0 — storico

Prima di consegnare 0.6.0: compilare i due publish win-x64 e MSI su Windows, eseguire test framing e regressione, installare su VM, verificare boot senza logon e stato reale da account standard, ACL/pipe squatting/client remoto/impersonazione, timeout/input ostile/RDP/logoff, recovery dopo crash e install/repair/upgrade/rollback/uninstall. Provare che chiudere Tray lascia heartbeat attivo e che un utente standard non modifica/disinstalla il servizio. Nessuna di queste prove è dichiarata eseguita qui.

Da sviluppare: Service Desk e identità richiedente verificata, ticket propri, allegati con anteprima/consenso, consenso remoto end-to-end, dismissione integrata e audit anomalie persistente. Le capacità IT restano separate e autorizzate sul backend; nessun ticket futuro o consenso Tray implica automaticamente remote.control/terminal/files.

## Aggiornamento ticket e compilazione

Il precedente impedimento Docker è superato: Agent/Tray 0.6.0 cross-compilati su Linux. La pipe ora aggiunge CreateTicket/ListTickets, mantenendo identificazione logon/ACL/verifica server; limite separato per messaggi assistenza. La Tray include richiesta, elenco proprio, anteprima e consenso screenshot. Vedere [implementazione supporto](support-implementation.md) per confini, limiti e autorizzazioni. Le descrizioni iniziali read-only/nessun ticket di questo documento rappresentano lo stato precedente. Nessuna attestazione runtime Windows deriva dalla cross-compilazione.
