# Rilevamento Defender del motore Windows: dossier di verifica

Stato: blocco confermato sul client pilota; revisione del rilevamento non ancora
inviata a Microsoft e nessun verdetto di falso positivo acquisito.

## Evidenza del collaudo

Windows 10, eventi Defender 1116/1117 del 19 ottobre 2026, ore locali 09:23:
- Nome: HackTool:Win32/Meshagent.MBZ!MTB; threat ID 2147970383.
- Rilevamento in tempo reale, gravita Alta, azione quarantena completata.
- Risorse: MeshService64.exe e servizio OmniDeskRemote.
- Intelligence: 1.461.100.0; motore: 1.1.26090.9.
- La riparazione MSI aveva ripristinato file e servizio; la quarantena successiva
  spiega la loro nuova assenza e la mancata associazione. L'Agent principale
  continua a inviare heartbeat. Nessuna sessione remota funzionante attestata.

## Provenienza del campione distribuito

- Dipendenza bloccata: pacchetto npm meshcentral 1.2.5, lockfile repository.
- Percorso upstream: agents/MeshService64.exe.
- SHA256 atteso nel build e nel worker:
  cbf10a37fc01f74f2473aeca0a4b391dca6f9d0a8d0300aa7181dbce7bacc51e
- Il build verifica tale hash prima di inserire il binario nell'MSI; il worker
  verifica ACL e hash prima dell'avvio. Questo dimostra corrispondenza col
  campione scelto, non innocuita o correttezza del rilevamento antivirus.
- Pacchetto OmniDesk di laboratorio unsigned, non una release di produzione.

## Passaggi necessari

Raccogliere campione originale e metadati senza credenziali, configurazioni MSH,
database macchina o token; sottoporre il solo binario a revisione del produttore
antivirus come sviluppatore e confrontare l'esito con upstream. La procedura
Microsoft e documentata qui:
https://learn.microsoft.com/en-us/unified-secops/submission-guide
Portale: https://www.microsoft.com/en-us/wdsi/filesubmission

Questa preparazione non costituisce un invio: nessun file o messaggio e stato
trasmesso a terzi. Prima di sostituire il motore verificare provenienza, licenza,
hash, firma disponibile e compatibilita; nuova dipendenza richiede nuovo ciclo
di build/test. Un cambio nome, impacchettamento o firma non prova la risoluzione
del rilevamento. Non inserire nel prodotto modifiche automatiche alle policy
antivirus o ripristino automatico di file messi in quarantena.

Riprendere il collaudo remoto solo dopo aver risolto la valutazione del binario
nel contesto autorizzato; verificare su Windows reale mapping, consenso e revoca.

## Preparazione invio del campione (19 ottobre 2026)

Campione upstream recuperato dal runtime MeshCentral installato sul server e
verificato senza eseguirlo: `artifacts/microsoft-submission/MeshService64.exe`,
3,444,736 byte, SHA256 uguale al valore atteso sopra.
`artifacts/microsoft-submission/submission-details.txt` contiene campi e testo
in inglese da incollare nel portale. Il solo EXE è stato estratto; nessun MSI,
chiave, token o configurazione inclusi. File locale ignorato da Git e con ACL
ristretta. Il portale Microsoft richiede account sviluppatore, CAPTCHA e invio
esplicito; inoltre richiede di accettare i termini che indicano conservazione
negli Stati Uniti. Non risultano credenziali Microsoft o interazione autenticata
nel workspace: preparazione completata, invio a Microsoft non ancora trasmesso.
