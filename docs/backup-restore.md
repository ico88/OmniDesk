# Backup e verifica ripristino

Disponibili backup locale e prova di ripristino **del database** su PostgreSQL effimero. Non costituiscono ancora un disaster recovery completo: ripristino delle chiavi Identity/CA nell'applicazione, storage Mesh opzionale, cifratura dei backup, copia off-site, retention e tempi RPO/RTO restano da collaudare/configurare.

## Backup dell'istanza standard

Eseguire dalla root del progetto, con container DB/API/Caddy avviati:

```bash
bash scripts/backup.sh
```

Lo script crea una directory casuale datata sotto `.backups`, con permessi riservati. Contiene dump PostgreSQL custom, chiavi Identity, dati Caddy incluse chiavi private CA, configurazione ambiente, password DB e configurazione Compose/Caddy. Registra gli identificativi delle immagini in esecuzione e la revisione Git HEAD; modifiche non committate e sorgenti vanno conservati separatamente. Non include la password bootstrap né gli eventuali volumi MeshCentral. `.backups` è esclusa da Git e dal contesto Docker.

Questi file contengono segreti: trasferirli solo su storage riservato e cifrato. Il backup locale sullo stesso disco non protegge dal guasto del server. Non pubblicare il contenuto tramite la dashboard. Nessuna pianificazione o cancellazione automatica viene attivata.

`pg_dump` usa uno snapshot coerente del DB; le chiavi/configurazioni vengono copiate successivamente. Non ruotare credenziali o CA, modificare MFA/Identity, configurazione o deployment durante il backup. Non viene promessa atomicità tra PostgreSQL e filesystem; una futura funzione allegati richiederà una strategia coerente DB/blob. Non fermare i servizi di altri prodotti sul server.

Il file `READY` è scritto solo a completamento riuscito; un dump parziale conserva estensione `.partial`. Il checksum copre il dump, non autentica l'intero backup e non sostituisce una firma. Gli archivi incompleti restano disponibili per diagnosi e non vengono accettati dal verificatore.

## Prova non distruttiva del database

```bash
bash scripts/verify-backup.sh .backups/<directory-del-backup>
```

Verifica checksum e importa il dump con `pg_restore --exit-on-error` in un container PostgreSQL 17 temporaneo: nome progetto univoco, rete disabilitata, nessuna porta host, dati su tmpfs e dump montato in sola lettura. Le password e i dati di produzione non vengono stampati; solo conteggi migrazioni/account/dispositivi. Il container viene rimosso alla fine. Non importa dati nel database di produzione e non avvia un'API sul database ripristinato.

## Recupero completo — procedura da collaudare

Ripristinare inizialmente su host isolato e usare la configurazione/versione di origine: creare volumi nuovi, importare il dump, ripristinare le chiavi Identity e la CA mantenendo ACL e ownership, recuperare configurazione/segreti tramite canale riservato. Prima di esporre il servizio verificare schema, login/password/TOTP/recovery, cookie Secure/CSRF, autorizzazioni, TLS e identità dispositivo. Conservare le chiavi Identity originali: generarne altre rende illeggibili i token cifrati già presenti. Ripristinare la CA evita di dover distribuire un nuovo trust ai client.

Applicare eventuali migrazioni solo dopo una verifica della compatibilità con le immagini scelte; provare rollback su copie, senza cancellare i volumi originari. Non sovrascrivere il DB live o le sue chiavi come parte della semplice verifica. I job/sessioni ripristinati richiedono riconciliazione: il restore di una fotografia storica non riattiva automaticamente autorizzazioni revocate o operazioni remote. La procedura completa non è ancora attestata operativa.
