# M4 — Windows Update

Implementati job persistenti scan/install per dispositivo con permesso updates indipendente da view/remote. RequestId idempotente, singolo job attivo per dispositivo, lease atomico 65 minuti, risultati autenticati dall'identità device e leaseId. Nessuna reinstallazione automatica di job con lease scaduta: stato failed, controllo umano prima di ripetere. Cancellazione solo queued; un job già avviato non può essere annullato da API. Risultati idempotenti, audit queue/claim/result/cancel.

Agente: worker separato dal heartbeat, esegue script fisso incorporato usando API COM Windows Update (Microsoft.Update.Session), mai script/comandi forniti dal server. Lo script esegue scansione software non installato/non nascosto secondo le policy Windows/WSUS presenti, poi scarica/installa solo ID e revisione selezionati. Non modifica WSUS, firewall o policy Windows; non bypassa ExecutionPolicy. Accetta EULA solo degli aggiornamenti approvati. Nessun riavvio automatico; rebootRequired riportato. Ricevuta locale persistente impedisce riesecuzione dopo crash; risultati ritentati senza rieseguire installazione. Timeout processo 60 minuti: l'interruzione del processo non garantisce arresto delle operazioni COM già affidate a Windows Update, da verificare sul dispositivo.

API: GET /api/devices/{id}/updates; POST /updates/scan {requestId}; POST /updates/install {requestId,updates:[{updateId,revision}]}; DELETE /updates/{jobId}. Machine: POST /api/agent/jobs/claim, POST /api/agent/jobs/{id}/result. Solo JWT device valido può claim/ack; il payload non può scegliere un altro dispositivo.

Dashboard: scansione, aggiornamento stato, selezione aggiornamenti, installazione esplicita, cancellazione queued e risultato. Nessuna simulazione dei risultati. Pianificazione, maintenance windows e approvazioni organizzative aggiuntive future.

**Verifiche Windows ancora necessarie**: WUA/COM e PowerShell non possono essere eseguiti sull'host Linux; nessun aggiornamento reale è stato installato. Il test backend usa risultati fixture e verifica lo stato della coda, non il successo di Windows Update. Script e worker restano candidati da collaudare su VM Windows isolata prima di uso operativo.

Fonti: [IUpdateSession](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iupdatesession), [WUA API](https://learn.microsoft.com/en-us/windows/win32/api/_wua/).
