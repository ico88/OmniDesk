# Retention dei dati

La policy elimina i payload tecnici dell'outbox consegnati e i messaggi dell'inbox email di intake. Con la configurazione standard, OmniDesk elimina dopo 180 giorni gli eventi outbox per cui esiste almeno una consegna e tutte le consegne risultano completate senza dead-letter. I messaggi email ancora `pending` vengono eliminati dopo 30 giorni; quelli `discarded` o `promoted` dopo 180 giorni. Il riferimento al ticket promosso viene cancellato con la riga intake (`PromotedTicketId` diventa nullo), mentre ticket, conversazione ed eventuale copia del testo email nel ticket seguono policy separate. La data usata è `ReceivedAt` registrata dal server, non la data dichiarata dal mittente. Stati email sconosciuti sono conservati per default.

Il batch è limitato a 250 righe, fino a dieci batch per passaggio, eseguito all'avvio e poi ogni 24 ore. La cancellazione dell'evento outbox elimina in cascata ricevute e notifiche utente collegate. Ogni batch scrive nell'audit soltanto il conteggio (`retention.outbox.purged` o `retention.email-intakes.purged`), senza payload, indirizzi, oggetti, identificativi dei messaggi o eventi.

Gli eventi in retry, in dead-letter o senza consumer non vengono eliminati. Il worker rivalida le condizioni nella query di cancellazione; ogni batch e il relativo record audit sono transazionali. Il valore `0` disattiva la pulizia; sono accettati valori da 0 a 36500 giorni.

Configurazione Compose:

```dotenv
OMNIDESK_RETENTION_DELIVERED_OUTBOX_DAYS=180
OMNIDESK_RETENTION_PROCESSED_EMAIL_INTAKES_DAYS=180
OMNIDESK_RETENTION_PENDING_EMAIL_INTAKES_DAYS=30
OMNIDESK_RETENTION_DEVICE_METRICS_DAYS=0
OMNIDESK_RETENTION_DEVICE_INVENTORY_DAYS=0
```

I periodi sono compresi fra 0 e 36500 giorni; `0` disattiva ciascuna policy opt-in. Outbox e intake mantengono i default storici di 180/30 giorni indicati sopra. La retention delle metriche elimina lo snapshot scaduto solo se il dispositivo risulta già offline; l'inventario usa la propria soglia e nessuna policy elimina il record dispositivo. I batch sono limitati a 250 righe per ciclo e dieci batch per passaggio. Le azioni `retention.device-metrics.purged` e `retention.device-inventory.purged` registrano solo il conteggio. I valori sono scelte operative, non regole legali. Queste policy non eliminano ticket, messaggi ticket, note interne, screenshot, allegati, sessioni remote, job o articoli KB. Le copie già incluse nei backup rimangono soggette alla retention e protezione dei backup; la pulizia live non ne garantisce l'eliminazione. Il mantenimento dei dati non è una garanzia di conservazione illimitata né di conformità a requisiti esterni.

La pulizia può essere verificata tramite le azioni audit `retention.outbox.purged`, `retention.email-intakes.purged`, `retention.device-metrics.purged` e `retention.device-inventory.purged`; il conteggio è il solo dato registrato. I test PostgreSQL coprono gli eventi non consegnati, gli intake pending e terminali, metriche online/offline, snapshot recenti, stati sconosciuti, record dispositivo preservati e audit privo di contenuti. Le notifiche utente hanno una FK con cascade sull'evento. Restore isolato e retention dei backup restano necessari prima di modificare policy che includano contenuti o blob.

## Backup locali

`scripts/backup.sh` può eliminare i backup locali verificati oltre una soglia configurabile. `OMNIDESK_BACKUP_RETENTION_DAYS` accetta `0` (default, disattivata) oppure da 1 a 36500 giorni; si può impostare nell'ambiente del comando o nel `.env` del deployment. La pulizia avviene soltanto dopo che il nuovo backup è stato marcato `READY` e dopo la verifica DB/blob/restore. Vengono considerati soltanto i figli diretti di `.backups` con nome prodotto dallo script e marker `READY` regolare; directory in staging, symlink e file fuori da `.backups` restano intatti. Il log riporta solo il numero di backup rimossi.

Esempio esplicito per conservare 90 giorni:

```dotenv
OMNIDESK_BACKUP_RETENTION_DAYS=90
```

Questa opzione non cifra né copia i backup fuori host. I backup contengono credenziali e chiavi private: proteggere `.backups` con ACL/permessi adeguati e predisporre separatamente cifratura e destinazione off-site prima dell'uso come strategia di disaster recovery.
