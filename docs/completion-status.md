# Stato corrente di consegna

**Aggiornato il 2026-10-20.** Questo riepilogo descrive il branch locale `sviluppo` e l'ultimo deploy registrato in [validation.md](validation.md). Gli esiti storici e i comandi di verifica restano nel registro di validazione; non usare le vecchie versioni citate nelle note precedenti come stato live.

## Stato attuale

| Area | Stato consegnato | Verifiche registrate | Gate aperti |
| --- | --- | --- | --- |
| API e dashboard | Onboarding dispositivi, gestione ticket manuale, console RustDesk e panoramica operativa sono distribuiti; l'operatore ha confermato schermate e collegamento dal browser. La panoramica mostra stato heartbeat, conteggi online/offline/revocati e contatti recenti. Rimossi i tab Terminale/PowerShell, Trasferimento file, Policy SLA, email in ingresso e workflow ticket. | Build dashboard e Compose validi; readiness API `ready`; bundle live `index-992vizvv.js` e `index-DIhVAyuS.css` contengono la nuova panoramica. | Verifica visuale della nuova panoramica nel browser. |
| RustDesk OSS | Client Windows 1.5.0 disponibile agli amministratori con hash verificato. OmniDesk mostra ID server, relay e chiave, salva il mapping peer/device, controlla `remote.control`, registra l'avvio e apre il client RustDesk locale. L'operatore ha confermato il collegamento. Trasporto TCP 21115–21117 e UDP 21116 pubblicato sul router. | Container healthy; porte TCP raggiungibili dal PC remoto; apertura dal browser e collegamento confermati dall'operatore. | Il resto della sessione è gestito dal client esterno: OmniDesk non ne osserva lo stato né gestisce consenso, disconnessione o revoca. Provisioning automatico e lifecycle integrato non implementati. |
| Dispositivi e Agent | Il modale **Aggiungi dispositivo** guida download Agent e registrazione. Kit unsigned 0.8.16 pubblicato; il nuovo Agent raccoglie seriale BIOS e previsione SMART per disco quando esposta da Windows. | CI Linux [39499614555](https://github.com/ico88/OmniDesk/actions/runs/39499614555) e Windows [39499614562](https://github.com/ico88/OmniDesk/actions/runs/39499614562) verdi. Manifest live aggiornato e SHA-256 del ZIP verificato dal container API; readiness API `ready`. | Collaudare sul PC pilota e confermare raccolta SMART con i dischi/controller effettivi. Firma Authenticode e revisione Microsoft Defender restano gate per una release pubblica. |
| Assistenza e moduli admin | Il pannello Assistenza conserva la gestione manuale ticket; nascoste le interfacce Policy SLA, email in ingresso e workflow. Ricerca, filtri e azioni ticket restano disponibili. | Build dashboard e bundle aggiornato distribuito; readiness API `ready`. | Verifica visuale della nuova navigazione. |
| Terminale e file | I tab Terminale/PowerShell e Trasferimento file sono stati rimossi dalla console; l'operatore usa il client RustDesk esterno per l'intervento remoto. | Bundle dashboard aggiornato servito dal server; le etichette dei tab non compaiono nel JS live. | Le API e le capability Agent preesistenti restano nel server, ma non sono esposte nella console. |
| Microsoft Defender e firma | Integrazione Defender solo progettata. Il pacchetto Agent è unsigned da laboratorio. | Specifica documentata; nessuna esclusione o policy Windows è stata modificata da OmniDesk. | Implementazione distinta, firma Authenticode e revisione del rilevamento Defender prima di una release pubblica. |
| Backup e Service Desk | Funzioni già implementate sono descritte nelle rispettive sezioni della roadmap e nel registro di validazione; le policy di retention che hanno default `0` restano disattivate. | Esiti per singola milestone in [validation.md](validation.md). | Account cliente/provider reali, restore off-site, SMTP reale e policy di produzione dove indicato nelle milestone. |

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
