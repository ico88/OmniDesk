# Backup e verifica ripristino

Disponibili backup locale, verifica DB/blob e restore applicativo isolato con chiavi Identity e CA Caddy. Non costituiscono ancora un disaster recovery completo: storage Mesh opzionale, cifratura dei backup, copia off-site, retention, consistenza allegati, failover e tempi RPO/RTO restano da collaudare/configurare.

## Backup dell'istanza standard

Eseguire dalla root del progetto, con container DB/API/Caddy avviati:

```bash
bash scripts/backup.sh
```

Lo script crea una directory casuale datata sotto `.backups`, con permessi riservati. Contiene dump PostgreSQL custom, blob allegati, chiavi Identity, dati Caddy incluse chiavi private CA, configurazione ambiente, password DB e configurazione Compose/Caddy. Registra gli identificativi delle immagini in esecuzione e la revisione Git HEAD; modifiche non committate e sorgenti vanno conservati separatamente. Non include la password bootstrap né gli eventuali volumi MeshCentral. `.backups` è esclusa da Git e dal contesto Docker.

Questi file contengono segreti: trasferirli solo su storage riservato e cifrato. Il backup locale sullo stesso disco non protegge dal guasto del server. Non pubblicare il contenuto tramite la dashboard. Nessuna pianificazione o cancellazione automatica viene attivata.

`pg_dump` acquisisce lo snapshot DB prima della copia immutabile dei blob allegati. I blob sono scritti e sincronizzati su disco prima che la riga DB venga confermata e l'applicazione non elimina allegati: questo ordinamento include nel backup ogni blob referenziato dallo snapshot; eventuali file creati dopo lo snapshot possono comparire come orfani. Il manifest SHA-256 copre tutti i file copiati. Non ruotare credenziali o CA, modificare MFA/Identity, configurazione o deployment durante il backup. La procedura non garantisce atomicità contro cancellazioni manuali o guasti del filesystem; il recupero applicativo DB/blob deve essere collaudato insieme. Non fermare i servizi di altri prodotti sul server.

Il file `READY` è scritto solo a completamento riuscito; un dump parziale conserva estensione `.partial`. I nuovi backup hanno `files.sha256` per tutti i file regolari raccolti. I checksum rilevano corruzione, ma il manifest non è firmato e non autentica l'origine. I backup precedenti senza manifest sono accettati con avviso; restano verificati i checksum specifici di dump e allegati. Gli archivi incompleti restano disponibili per diagnosi e non vengono accettati dal verificatore.

## Prova non distruttiva del database

```bash
bash scripts/verify-backup.sh .backups/<directory-del-backup>
```

Verifica checksum e importa il dump con `pg_restore --exit-on-error` in un PostgreSQL 17 temporaneo: progetto univoco, rete disabilitata, nessuna porta host, dati su tmpfs e dump in sola lettura. Per i backup con blob verifica anche i riferimenti tra righe e file. Quando sono presenti chiavi Identity, dati Caddy e password DB, avvia una copia applicativa su rete Docker interna isolata, ripristina i volumi, applica le migrazioni alla copia e verifica readiness, diniego anonimo, antiforgery e HTTPS con la CA ripristinata. I volumi temporanei vengono rimossi al termine. Non modifica il DB live.

## Limiti e procedura di recupero

Ripristinare inizialmente su host isolato e usare la configurazione/versione di origine: creare volumi nuovi, importare il dump, ripristinare le chiavi Identity e la CA mantenendo ACL e ownership, recuperare configurazione/segreti tramite canale riservato. Prima di esporre il servizio verificare schema, login/password/TOTP/recovery, cookie Secure/CSRF, autorizzazioni, TLS e identità dispositivo. Conservare le chiavi Identity originali: generarne altre rende illeggibili i token cifrati già presenti. Ripristinare la CA evita di dover distribuire un nuovo trust ai client.

Prova runtime eseguita sul backup storico `.backups/20261019T153639Z.mzZpdG`: dump importato, 29 migrazioni storiche applicate alla copia con l'immagine API locale corrente, chiavi Identity/CA ripristinate, API pronta, accesso anonimo negato, cookie antiforgery Secure emesso su HTTPS. Quel backup precede l'inclusione dei blob e del manifest generale: controllo allegati e integrità completa dell'archivio non sono coperti. Il volume Mesh non è stato avviato né verificato.

Per un recupero reale, applicare eventuali migrazioni solo dopo verifica compatibilità e provare rollback su copie senza cancellare i volumi originari. Non sovrascrivere il DB live o le sue chiavi durante la verifica. I job/sessioni ripristinati richiedono riconciliazione: una fotografia storica non riattiva automaticamente autorizzazioni revocate o operazioni remote. Il restore isolato non sostituisce una prova periodica di disaster recovery su host separato.
