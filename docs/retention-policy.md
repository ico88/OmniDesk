# Retention dei dati

La policy elimina i payload tecnici dell'outbox consegnati e i messaggi dell'inbox email di intake. Con la configurazione standard, OmniDesk elimina dopo 180 giorni gli eventi outbox per cui esiste almeno una consegna e tutte le consegne risultano completate senza dead-letter. I messaggi email ancora `pending` vengono eliminati dopo 30 giorni; quelli `discarded` o `promoted` dopo 180 giorni. Il riferimento al ticket promosso viene cancellato con la riga intake (`PromotedTicketId` diventa nullo), mentre ticket, conversazione ed eventuale copia del testo email nel ticket seguono la retention dei ticket, ancora da definire. La data usata è `ReceivedAt` registrata dal server, non la data dichiarata dal mittente. Stati email sconosciuti sono conservati per default.

Il batch è limitato a 250 righe, fino a dieci batch per passaggio, eseguito all'avvio e poi ogni 24 ore. La cancellazione dell'evento outbox elimina in cascata ricevute e notifiche utente collegate. Ogni batch scrive nell'audit soltanto il conteggio (`retention.outbox.purged` o `retention.email-intakes.purged`), senza payload, indirizzi, oggetti, identificativi dei messaggi o eventi.

Gli eventi in retry, in dead-letter o senza consumer non vengono eliminati. Il worker rivalida le condizioni nella query di cancellazione; ogni batch e il relativo record audit sono transazionali. Il valore `0` disattiva la pulizia; sono accettati valori da 0 a 36500 giorni.

Configurazione Compose:

```dotenv
OMNIDESK_RETENTION_DELIVERED_OUTBOX_DAYS=180
OMNIDESK_RETENTION_PROCESSED_EMAIL_INTAKES_DAYS=180
OMNIDESK_RETENTION_PENDING_EMAIL_INTAKES_DAYS=30
```

I periodi devono essere compresi fra 1 e 36500 giorni; `DeliveredOutboxDays=0` disattiva solo la pulizia outbox. I valori sono scelte operative, non regole legali. La retention email non elimina ticket, messaggi ticket, note interne, screenshot, allegati, audit, sessioni remote, job, inventario o articoli KB. Questi dati richiedono policy separate con accesso, backup, cancellazione coordinata DB/blob e audit verificabili. Le copie già incluse nei backup rimangono soggette alla retention e protezione dei backup; la pulizia live non ne garantisce l'eliminazione. Il mantenimento di ticket e allegati non è una garanzia di conservazione illimitata né di conformità a requisiti esterni.

La pulizia può essere verificata tramite le azioni audit `retention.outbox.purged` e `retention.email-intakes.purged`; il conteggio è il solo dato registrato. I test PostgreSQL coprono eventi non ancora consegnati, l'intake vecchio pending e terminale, righe recenti, stati sconosciuti e audit privo di contenuti. Le notifiche utente hanno una FK con cascade sull'evento. Restore isolato e retention dei backup restano necessari prima di modificare policy che includano contenuti o blob.
