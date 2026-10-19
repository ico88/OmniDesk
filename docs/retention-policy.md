# Retention dei dati

La policy attiva copre soltanto il payload tecnico dell'outbox già consegnato. Con la configurazione standard, OmniDesk elimina dopo 180 giorni gli eventi outbox per cui esiste almeno una consegna e tutte le consegne risultano completate senza dead-letter. Il batch è limitato a 250 eventi, fino a dieci batch per passaggio, eseguito all'avvio e poi ogni 24 ore. La cancellazione dell'evento elimina in cascata le sue ricevute e notifiche utente collegate. Ogni batch completato scrive `retention.outbox.purged` nell'audit con il solo conteggio, senza payload o identificativi degli eventi.

Gli eventi in retry, in dead-letter o senza consumer non vengono eliminati. Il worker rivalida le condizioni nella query di cancellazione; ogni batch e il relativo record audit sono transazionali. Il valore `0` disattiva la pulizia; sono accettati valori da 0 a 36500 giorni.

Configurazione Compose:

```dotenv
OMNIDESK_RETENTION_DELIVERED_OUTBOX_DAYS=180
```

La variabile è una scelta operativa, non una regola legale. La pulizia dell'outbox non elimina ticket, messaggi, note interne, screenshot, allegati, audit, sessioni remote, job, inventario o articoli KB. Questi dati restano conservati finché non viene approvata e implementata una policy specifica con accesso, backup, cancellazione coordinata DB/blob e audit verificabili. In particolare, il mantenimento di allegati e ticket non va interpretato come garanzia di conservazione illimitata né come periodo conforme a requisiti esterni.

La pulizia può essere verificata cercando l'azione audit `retention.outbox.purged`; il conteggio è il solo dato registrato. Il test PostgreSQL copre evento completato, evento recente, retry, dead-letter, evento senza consumer, eliminazione delle ricevute collegate e audit. Le notifiche utente hanno una FK con cascade sull'evento. Il restore isolato resta necessario prima di modificare policy che includano contenuti o blob.
