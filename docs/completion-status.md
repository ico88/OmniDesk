# Stato corrente di consegna

**Aggiornato il 2026-10-21.** Questo riepilogo descrive il branch locale `sviluppo` e l'ultimo deploy registrato in [validation.md](validation.md). Gli esiti storici e i comandi di verifica restano nel registro di validazione; non usare le vecchie versioni citate nelle note precedenti come stato live.

## Stato attuale

| Area | Stato consegnato | Verifiche registrate | Gate aperti |
| --- | --- | --- | --- |
| API e dashboard | Onboarding dispositivi, gestione ticket manuale, console RustDesk, panoramica operativa e scheda Defender per dispositivo sono distribuiti. Rimossi i tab Terminale/PowerShell, Trasferimento file, Policy SLA, email in ingresso e workflow ticket. | Readiness HTTPS `ready`; bundle live `index-Cycb2mPF.js` e `index-BIpQyedN.css`; permesso Defender assegnato al ruolo Administrator e migrazione `20261021113000_DefenderReadOnlyPermission` presente. | PC pilota non ha ancora inviato un report Defender 0.8.17; scheda quindi senza dati finché Agent non è aggiornato. |
| Recupero password admin | Comando CLI con ASP.NET Identity reimposta un Administrator attivo ricevendo la password su standard input; non la legge né stampa e registra audit. | API distribuita; nessun account è stato reimpostato da Codex. | Eseguire la procedura documentata solo se serve recuperare l'accesso. |
| RustDesk OSS | Client Windows 1.5.0 disponibile agli amministratori con hash verificato. OmniDesk mostra ID server, relay e chiave, salva il mapping peer/device, controlla `remote.control`, registra l'avvio e apre il client RustDesk locale. L'operatore ha confermato il collegamento. Trasporto TCP 21115–21117 e UDP 21116 pubblicato sul router. | Container healthy; porte TCP raggiungibili dal PC remoto; apertura dal browser e collegamento confermati dall'operatore. | Il resto della sessione è gestito dal client esterno: OmniDesk non ne osserva lo stato né gestisce consenso, disconnessione o revoca. Provisioning automatico e lifecycle integrato non implementati. |
| Dispositivi e Agent | Il modale **Aggiungi dispositivo** guida download Agent e registrazione. Kit unsigned 0.8.17 pubblicato; raccoglie seriale BIOS, previsione SMART e Defender read-only. Il follow-up 0.8.18 in lavorazione aggiunge health check post-installazione e rollback MSI limitato. | Test API/dashboard precedenti passati; pipeline Windows 0.8.17 passata e kit pubblicato. Il codice 0.8.18 e la UI diagnostica sono in verifica; nessun test su PC pilota per questi cambiamenti. | Completare CI Windows, pubblicare 0.8.18 e collaudare aggiornamento/rollback e dati hardware reali sul PC pilota. Firma Authenticode richiede certificato esterno; nessuna policy o esclusione è stata modificata. |
| Assistenza e moduli admin | Il pannello Assistenza conserva la gestione manuale ticket; nascoste le interfacce Policy SLA, email in ingresso e workflow. La schermata di apertura ticket della tray è stata ridisegnata nel sorgente con anteprima e consenso screenshot più evidenti. | Build dashboard e bundle precedente distribuito; il ridisegno tray attende CI Windows e deploy del nuovo kit. | Verifica visuale della tray sul PC pilota dopo installazione del kit aggiornato. |
| Terminale e file | I tab Terminale/PowerShell e Trasferimento file sono stati rimossi dalla console; l'operatore usa il client RustDesk esterno per l'intervento remoto. | Bundle dashboard aggiornato servito dal server; le etichette dei tab non compaiono nel JS live. | Le API e le capability Agent preesistenti restano nel server, ma non sono esposte nella console. |
| Microsoft Defender e firma | Snapshot read-only e scheda dispositivo 0.8.17 attivi in OmniDesk. Kit 0.8.17 unsigned pubblicato. Firma Authenticode non disponibile. | API/dashboard distribuite, migrazione applicata, test API e CI Windows MSI passati. Nessuna esclusione o policy Windows è stata modificata. | Verificare i dati Defender sul PC pilota. Il follow-up Agent 0.8.18 ha pipeline e deploy in corso; certificato esterno necessario per la firma Authenticode. |
| Backup server | La schermata **Backup server** è distribuita: imposta share SMB, credenziali, recipient age, orario/fuso e retention; la password è protetta con Data Protection. Gli script backup usano il progetto Compose production e includono dati RustDesk. | Backup pre-deploy eseguito e restore isolato completo verificato. `age 1.2.1` e `smbclient 4.23.6` installati; endpoint admin anonimo restituisce 401. | Installare e attivare il timer host; inserire share e recipient age reali; verificare upload notturno e restore decifrato dal NAS. |
| Service Desk | Funzioni già implementate sono descritte nelle rispettive sezioni della roadmap e nel registro di validazione; le policy di retention che hanno default `0` restano disattivate. | Esiti per singola milestone in [validation.md](validation.md). | Account cliente/provider reali e policy di produzione dove indicato nelle milestone. |

## Come leggere lo stato

- **Distribuito** significa che il deploy è registrato in `validation.md`; non implica una prova visuale o end-to-end con utente reale.
- **Build/CI passata** non equivale a collaudo sul PC Windows o a una sessione remota effettiva.
- Le sezioni datate della [roadmap](roadmap.md) e di `validation.md` sono cronologia incrementale. Se riportano una versione o uno stato precedente, fa fede questo snapshot insieme alla voce di validazione più recente.
- L'operatore ha confermato le schermate correnti e il collegamento RustDesk reale. I tab Terminale/PowerShell e Trasferimento file sono rimossi e la dashboard aggiornata è distribuita.


## Accesso a due passaggi email (2026-10-20)

Flusso password → codice temporaneo email a sei cifre → sessione implementato,
con challenge monouso, hash del codice/token, scadenza, limite tentativi,
reinoltro limitato, audit senza segreti e UI dedicata. Il supporto è deployato
ma il flag resta `OMNIDESK_EMAIL_2FA_ENABLED=false`: l’accesso corrente non
richiede il codice. Attivazione richiede SMTP outbound valido e scelta
amministrativa esplicita. Esiti e limiti in [validation.md](validation.md).
